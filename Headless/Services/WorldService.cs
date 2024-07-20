using System.Collections.Concurrent;
using FrooxEngine;
using SkyFrost.Base;
using Headless.Extensions;

namespace Headless.Services;

public class WorldService
{
    private readonly ILogger<WorldService> _logger;
    private readonly Engine _engine;
    private readonly ConcurrentDictionary<string, RunningSession> _runningWorlds;
    private readonly IConfigService _configService;

    public WorldService
    (
        ILogger<WorldService> logger,
        Engine engine,
        IConfigService configService
    )
    {
        _logger = logger;
        _engine = engine;
        _configService = configService;
        _runningWorlds = new();
    }

    public RunningSession? GetSession(string id)
    {
        if (!_runningWorlds.ContainsKey(id)) return null;
        return _runningWorlds[id];
    }

    public IEnumerable<RunningSession> ListAll()
    {
        return _runningWorlds.Values.AsEnumerable();
    }

    public async Task<RunningSession?> StartWorldAsync
    (
        WorldStartupParameters startupParameters,
        CancellationToken ct = default
    )
    {
        startupParameters.CustomSessionId = SanitizeSessionID(startupParameters.CustomSessionId);
        World? startedWorld;
        try
        {
            var startSettings = await startupParameters.GenerateStartSettings();
            startSettings.CreateLoadIndicator = false;
            startedWorld = await Userspace.OpenWorld(startSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception generating world startup parameters:\n{ex}");
            return null;
        }
        if (startedWorld is null)
        {
            _logger.LogError("Failed start world");
            return null;
        }

        while (startedWorld.State is World.WorldState.Initializing)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
        if (startedWorld.State is World.WorldState.Failed)
        {
            _logger.LogError($"Failed world startup: {startedWorld.FailState}");
            return null;
        }

        startupParameters = await _engine.GlobalCoroutineManager.StartBackgroundTask(
            async () =>
            {
                await default(NextUpdate);
                return await startedWorld.SetParametersAsync(startupParameters, _logger);
            }
        );

        var sessionCancellation = new CancellationTokenSource();
        var session = new RunningSession(startupParameters, startedWorld, sessionCancellation);
        if (!_runningWorlds.TryAdd(startedWorld.SessionId, session))
        {
            throw new InvalidOperationException("Duplicate session ids");
        }

        var handler = _engine.GlobalCoroutineManager.StartTask(() => SessionHandlerAsync(session, sessionCancellation.Token));
        session.Handler = handler;

        var autoSpawnItems = _configService.Config.AutoSpawnItems;
        if (autoSpawnItems is not null)
        {
            _ = startedWorld.Coroutines.StartTask(async () => {
                foreach (var item in autoSpawnItems)
                {
                    await startedWorld.RootSlot.AddSlot("Headless Auto-Spawn").LoadObjectAsync(item);
                }
            });
        }
        return session;
    }

    public async Task StopWorldAsync(string sessionId)
    {
        if (!_runningWorlds.TryRemove(sessionId, out var runningSession))
        {
            return;
        }
        await runningSession.CancellationTokenSource.CancelAsync();
        await (runningSession.Handler ?? Task.CompletedTask);
    }

    private string? SanitizeSessionID(string sessionId)
    {
        var id = sessionId;
        if (string.IsNullOrWhiteSpace(id)){
            id = null;
        }
        if (id is not null)
        {
            // automatically add the session ID prefix if it's not already been provided.
            // custom session IDs must be in the form "S-U-<username>:<arbitrary>
            if (!id.StartsWith("S-"))
            {
                id = $"S-{id}";
            }
            if (!SessionInfo.IsValidSessionId(id))
            {
                _logger.LogWarning($"Invalid custom session ID: {id}");
                id = null;
            }
            var sessionIDOwner = SessionInfo.GetCustomSessionIdOwner(id);
            if (sessionIDOwner != _engine.Cloud.CurrentUser?.Id)
            {
                _logger.LogWarning($"Cannot use session ID that's owned by another user. Trying to use {id}, currently logged in as {_engine.Cloud.CurrentUser?.Id ?? "anonymous"}");
                id = null;
            }
        }
        return id;
    }

    private async Task SessionHandlerAsync(RunningSession runningSession, CancellationToken ct = default)
    {
        async Task RestartSessionAsync(RunningSession runningSession, CancellationToken cancellationToken)
        {
            var world = runningSession.WorldInstance;
            world.WorldManager.WorldFailed -= MarkAutoRecoverRestart;
            if (!world.IsDestroyed)
            {
                world.Destroy();
            }
            await default(NextUpdate);
            await StartWorldAsync(runningSession.StartInfo, cancellationToken);
        }

        async Task StopSessionAsync(RunningSession runningSession)
        {
            var world = runningSession.WorldInstance;
            if (world.SaveOnExit && Userspace.CanSave(world))
            {
                // wait for any pending syncs of this world
                while (!world.CorrespondingRecord.IsSynced)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                }
                _logger.LogInformation("Saving {World}", world.RawName);
                await Userspace.SaveWorldAuto(world, SaveType.Overwrite, true);
            }
            world.WorldManager.WorldFailed -= MarkAutoRecoverRestart;
            if (!world.IsDestroyed)
            {
                world.Destroy();
            }
        }

        var restart = false;
        var world = runningSession.WorldInstance;
        var autoRecover = runningSession.StartInfo.AutoRecover;

        void MarkAutoRecoverRestart(World failedWorld)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (world.SessionId != failedWorld.SessionId)
            {
                return;
            }

            if (autoRecover)
            {
                restart = true;
                _logger.LogWarning("World {World} has crashed! Restarting...", failedWorld.RawName);
            }
            else
            {
                _logger.LogWarning("World {World} has crashed!", failedWorld.RawName);
            }
        }

        world.WorldManager.WorldFailed += MarkAutoRecoverRestart;

        while (!ct.IsCancellationRequested && !world.IsDestroyed)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var originalRunningSession = runningSession;

            if (runningSession.HasAutosaveIntervalElapsed && Userspace.CanSave(world))
            {
                // only attempt a save if the last save has been synchronized and we're not shutting down
                if (world.CorrespondingRecord.IsSynced && !(Userspace.IsExitingApp || _engine.ShutdownRequested))
                {
                    _logger.LogInformation("Autosaving {World}", world.RawName);
                    await Userspace.SaveWorldAuto(world, SaveType.Overwrite, false);
                }
                runningSession = runningSession with
                {
                    LastSaveTime = DateTimeOffset.UtcNow
                };
            }
            runningSession = runningSession with
            {
                IdleBeginTime = world.UserCount switch
                {
                    1 when runningSession.LastUserCount > 1 => DateTimeOffset.UtcNow,
                    > 1 => null,
                    _ => runningSession.IdleBeginTime
                }
            };

            if (runningSession.HasIdleTimeElapsed)
            {
                _logger.LogInformation
                (
                    "World {World} has been idle for {Time} seconds, restarting",
                    world.RawName,
                    (long)runningSession.TimeSpentIdle.TotalSeconds
                );

                restart = true;
                world.Destroy();

                break;
            }

            if (runningSession.HasForcedRestartIntervalElapsed)
            {
                _logger.LogInformation
                (
                    "World {World} has been running for {Time:1:F0} seconds, forcing a restart",
                    world.RawName,
                    runningSession.ForceRestartInterval.TotalSeconds
                );

                restart = true;
                world.Destroy();

                break;
            }

            runningSession = runningSession with
            {
                LastUserCount = world.UserCount
            };

            if (!_runningWorlds.TryUpdate(world.SessionId, runningSession, originalRunningSession))
            {
                _logger.LogError("Failed to update an active session's information ({World})! This is probably a concurrency bug", world.RawName);
            }
        }

        _logger.LogInformation("World {World} has stopped", world.Name);

        // always remove us first
        _runningWorlds.TryRemove(runningSession.WorldInstance.SessionId, out _);

        if (!ct.IsCancellationRequested && restart)
        {
            await RestartSessionAsync(runningSession, ct);
            return;
        }

        await StopSessionAsync(runningSession);
    }
}