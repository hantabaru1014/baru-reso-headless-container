using System.Collections.Concurrent;
using FrooxEngine;
using SkyFrost.Base;
using Headless.Extensions;
using Microsoft.Extensions.Options;
using Headless.Configuration;
using Headless.Models;

namespace Headless.Services;

public class WorldService
{
    private readonly ILogger<WorldService> _logger;
    private readonly Engine _engine;
    private readonly ConcurrentDictionary<string, RunningSession> _runningWorlds;

    public IEnumerable<Uri> AutoSpawnItems { get; set; }

    public WorldService
    (
        ILogger<WorldService> logger,
        IOptions<HeadlessStartupConfig> startupConfig,
        Engine engine
    )
    {
        _logger = logger;
        _engine = engine;
        _runningWorlds = new();

        if (startupConfig.Value.Value.AutoSpawnItems is not null)
        {
            AutoSpawnItems = startupConfig.Value.Value.AutoSpawnItems.Select(s =>
            {
                if (Uri.TryCreate(s, UriKind.Absolute, out var uri))
                {
                    return uri;
                }
                return null;
            }).Where(u => u != null).Select(u => u!);
        }
        else
        {
            AutoSpawnItems = new List<Uri>();
        }
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
        ExtendedWorldStartupParameters startupParameters,
        CancellationToken ct = default
    )
    {
        startupParameters.CustomSessionId = ValidateAndSanitizeSessionID(startupParameters.CustomSessionId);
        World? startedWorld;
        try
        {
            var startSettings = await startupParameters.GenerateStartSettings(_engine.PlatformProfile);
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
            _ = session.CancellationTokenSource.CancelAsync();
            // セッションIDはかぶらないはずなので、ひとつのセッションを2回入れようとしてる？ 起きないはず。
            throw new InvalidOperationException("Duplicate session ids");
        }

        var handler = _engine.GlobalCoroutineManager.StartTask(() => SessionHandlerAsync(session, sessionCancellation.Token));
        session.Handler = handler;

        if (AutoSpawnItems.Count() > 0)
        {
            _ = startedWorld.Coroutines.StartTask(async () =>
            {
                foreach (var item in AutoSpawnItems)
                {
                    await default(NextUpdate);
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

    public async Task StopAllWorldsAsync(CancellationToken ct = default)
    {
        var snapshot = _runningWorlds.Values;
        foreach (var runningSession in snapshot)
        {
            if (ct.IsCancellationRequested) break;

            await runningSession.CancellationTokenSource.CancelAsync();
            await (runningSession.Handler ?? Task.CompletedTask);
            _runningWorlds.TryRemove(runningSession.Instance.SessionId, out _);
        }
    }

    public async Task SaveWorldAsync(RunningSession runningSession)
    {
        if (!Userspace.ShouldSave(runningSession.Instance) || runningSession.IsWorldSaving) return;

        _logger.LogInformation("Saving {World}", runningSession.Instance.RawName);
        if (await runningSession.SaveWorld())
        {
            _logger.LogInformation("World({World}) saved successfully!", runningSession.Instance.RawName);
        }
        else
        {
            _logger.LogError("Failed world({World}) saving", runningSession.Instance.RawName);
        }
    }

    public async Task<FrooxEngine.Store.Record?> SaveWorldAsAsync(RunningSession runningSession, bool updateCurrentWorldRecord)
    {
        _logger.LogInformation("Saving As {World}", runningSession.Instance.RawName);
        var saved = await runningSession.SaveWorldCopy(runningSession.Instance.Engine.Cloud.CurrentUserID);
        if (saved != null)
        {
            if (updateCurrentWorldRecord)
            {
                runningSession.Instance.CorrespondingRecord = saved;
            }
            _logger.LogInformation("World({World}) saved as successfully!", runningSession.Instance.RawName);
        }
        else
        {
            _logger.LogError("Failed world({World}) saving as", runningSession.Instance.RawName);
        }
        return saved;
    }

    private string? ValidateAndSanitizeSessionID(string sessionId)
    {
        var id = sessionId;
        if (string.IsNullOrWhiteSpace(id))
        {
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
                throw new Exception($"Invalid custom session ID: {id}");
            }
            var sessionIDOwner = SessionInfo.GetCustomSessionIdOwner(id);
            if (sessionIDOwner != _engine.Cloud.CurrentUser?.Id)
            {
                throw new Exception($"Cannot use session ID that's owned by another user. Trying to use {id}, currently logged in as {_engine.Cloud.CurrentUser?.Id ?? "anonymous"}");
            }
        }
        return id;
    }

    private async Task SessionHandlerAsync(RunningSession runningSession, CancellationToken ct = default)
    {
        async Task RestartSessionAsync(RunningSession runningSession, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Restart Session");
            var world = runningSession.Instance;
            world.WorldManager.WorldFailed -= MarkAutoRecoverRestart;
            if (!world.IsDestroyed)
            {
                world.Destroy();
            }
            await default(NextUpdate);
            await StartWorldAsync(runningSession.GenerateStartupParameters(), cancellationToken);
        }

        async Task StopSessionAsync(RunningSession runningSession)
        {
            var world = runningSession.Instance;
            if (world.SaveOnExit)
            {
                if (runningSession.IsDirty)
                {
                    await SaveWorldAsync(runningSession);
                }
                else
                {
                    // TODO: SaveOnExitForceを作る?
                    _logger.LogInformation("Skipped world save due to world isnot dirty!");
                }
            }
            world.WorldManager.WorldFailed -= MarkAutoRecoverRestart;
            // これを待機したら一生終わらないので待たない。 Userspace.SaveWorldAuto でも待ってない。
            _ = Userspace.ExitWorld(world);
            if (!world.IsDestroyed)
            {
                world.Destroy();
            }
        }

        var restart = false;
        var world = runningSession.Instance;

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

            if (runningSession.AutoRecover)
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

            if (runningSession.HasAutosaveIntervalElapsed && Userspace.ShouldSave(runningSession.Instance))
            {
                // autoSaveは同期中や終了中には行わない
                if (!runningSession.IsWorldSaving && !(Userspace.IsExitingApp || _engine.ShutdownRequested))
                {
                    _logger.LogInformation("Autosaving {World}", world.RawName);
                    _ = runningSession.SaveWorld();
                }
            }
            runningSession.IdleBeganAt = world.UserCount switch
            {
                1 when runningSession.LastUserCount > 1 => DateTimeOffset.UtcNow,
                > 1 => null,
                _ => runningSession.IdleBeganAt
            };

            // ユーザがいなくなったタイミングで保存する
            // saveOnExitのタイミングをずらすことでshutdown時にコンテナをできるだけ早く終了させたい
            if (world.SaveOnExit && world.UserCount == 1 && runningSession.LastUserCount > 1)
            {
                _ = SaveWorldAsync(runningSession);
            }

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

            runningSession.LastUserCount = world.UserCount;
        }

        _logger.LogInformation("World {World} has stopped", world.Name);

        // always remove us first
        _runningWorlds.TryRemove(runningSession.Instance.SessionId, out _);

        if (!ct.IsCancellationRequested && restart)
        {
            await RestartSessionAsync(runningSession, ct);
            return;
        }

        await StopSessionAsync(runningSession);
    }
}
