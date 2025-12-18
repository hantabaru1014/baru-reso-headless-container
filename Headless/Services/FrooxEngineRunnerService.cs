using System.Reflection;
using FrooxEngine;
using PhotonDust;
using Awwdio;
using Headless.Configuration;
using Headless.Extensions;
using Microsoft.Extensions.Options;
using SkyFrost.Base;

namespace Headless.Services;

public interface IFrooxEngineRunnerService
{
    float TickRate { get; set; }
}

public class FrooxEngineRunnerService : BackgroundService, IFrooxEngineRunnerService
{
    private static Type? _type;

    private readonly ILogger<FrooxEngineRunnerService> _logger;
    private readonly ApplicationConfig _appConfig;
    private readonly Rpc.StartupConfig _startupConfig;
    private readonly Engine _engine;
    private readonly SystemInfo _systemInfo;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly WorldService _worldService;

    private bool _applicationStartupComplete;
    private bool _engineShutdownComplete;
    private float _tickRate = 60f;
    private PeriodicTimer _tickTimer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / 60f));

    public float TickRate
    {
        get => _tickRate;
        set
        {
            _tickRate = value;
            _tickTimer.Period = TimeSpan.FromSeconds(1.0 / value);
        }
    }

    private class EngineInitProgressLogger : IEngineInitProgress
    {
        private readonly ILogger _logger;

        public int FixedPhaseIndex { get; private set; }

        public EngineInitProgressLogger(ILogger logger)
        {
            _logger = logger;
        }

        public void SetFixedPhase(string phase)
        {
            ++FixedPhaseIndex;
            _logger.LogDebug(phase);
        }

        public void SetSubphase(string subphase, bool alwaysShow = false)
        {
            if (subphase is null) return;

            _logger.LogDebug($"\t{subphase}");
        }

        public void EngineReady()
        {
            _logger.LogInformation("Engine Ready!");
        }
    }

    public FrooxEngineRunnerService
    (
        ILogger<FrooxEngineRunnerService> logger,
        IOptions<ApplicationConfig> applicationConfig,
        IOptions<HeadlessStartupConfig> startupConfig,
        Engine engine,
        SystemInfo systemInfo,
        IHostApplicationLifetime applicationLifetime,
        WorldService worldService
    )
    {
        _logger = logger;
        _appConfig = applicationConfig.Value;
        _startupConfig = startupConfig.Value.Value;
        _engine = engine;
        _systemInfo = systemInfo;
        _applicationLifetime = applicationLifetime;
        _worldService = worldService;

        var config = startupConfig.Value.Value;
        if (config.HasTickRate && config.TickRate > 0)
        {
            TickRate = config.TickRate;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (ExecuteTask is null) return;
        try
        {
            var fieldInfo = typeof(BackgroundService).GetField("_stoppingCts", BindingFlags.Instance | BindingFlags.NonPublic);
            var tokenSource = fieldInfo!.GetValue(this) as CancellationTokenSource;
            await tokenSource!.CancelAsync();
        }
        finally
        {
            await ExecuteTask.ConfigureAwait(false);
        }
    }

    private void OnShutdownRequest(string message)
    {
        var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(_appConfig.ShutdownTimeoutSeconds * 1000);

        _ = StopAsync(tokenSource.Token);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        LoadTypes();

        var appVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
        _logger.LogInformation($"AppVersion: {appVersion}, ResoniteVersion: {_engine.VersionString}");

        if (_startupConfig.HasUsernameOverride && _startupConfig.UsernameOverride.Length > 0)
        {
            _engine.UsernameOverride = _startupConfig.UsernameOverride;
        }

        _engine.EnvironmentShutdownCallback = () => _engineShutdownComplete = true;
        _engine.OnShutdownRequest += OnShutdownRequest;
        var launchOptions = new LaunchOptions
        {
            OutputDevice = Renderite.Shared.HeadOutputDevice.Headless,
            DataDirectory = Path.Combine(_appConfig.DataDirectoryPath, "Data"),
            CacheDirectory = Path.Combine(_appConfig.DataDirectoryPath, "Cache"),
            LogsDirectory = null!,
            VerboseInit = true,
            NeverSaveSettings = true,
            NeverSaveDash = true,
            BackgroundWorkerCount = _appConfig.BackgroundWorkers,
            PriorityWorkerCount = _appConfig.PriorityWorkers,
        };

        await _engine.Initialize(
            AppDomain.CurrentDomain.BaseDirectory,
            false,
            launchOptions,
            _systemInfo,
            new EngineInitProgressLogger(_logger)
        );

        var userspaceWorld = Userspace.SetupUserspace(_engine);
        var engineLoop = EngineLoopAsync(ct);

        await userspaceWorld.Coroutines.StartTask(async () => await default(ToWorld));

        if (_startupConfig.HasUniverseId && _startupConfig.UniverseId.Length > 0)
        {
            Engine.Config.UniverseId = _startupConfig.UniverseId;
        }

        await LoginAsync(_appConfig.HeadlessUserCredential, _appConfig.HeadlessUserPassword);
        AllowHosts(_startupConfig.AllowedUrlHosts);

        if (_startupConfig.HasMaxConcurrentAssetTransfers && _startupConfig.MaxConcurrentAssetTransfers > 0)
        {
            SessionAssetTransferer.OverrideMaxConcurrentTransfers = _startupConfig.MaxConcurrentAssetTransfers;
        }

        var startWorlds = _startupConfig.StartWorlds.Select(w => w.ToResonite());
        foreach (var world in startWorlds)
        {
            if (!world.IsEnabled) continue;

            _logger.LogInformation($"Starting world : {world.SessionName ?? "NoName"} ({world.LoadWorldURL})");
            try
            {
                await _worldService.StartWorldAsync(world, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed start world: {0}", ex.Message);
            }
        }

        _applicationStartupComplete = true;
        _logger.LogInformation("Application startup complete");
        await engineLoop;
        _tickTimer.Dispose();
        _applicationLifetime.StopApplication();
    }

    private static void LoadTypes()
    {
        _type = typeof(ProtoFlux.Nodes.Core.Configuration);
        _type = typeof(FrooxEngine.ProtoFlux.ProtoFluxMapper);
        _type = typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.VoidNode<>);
        _type = typeof(ProtoFlux.Runtimes.Execution.Nodes.Math.TangentPointFloat);
        _type = typeof(FrooxEngine.Store.Record);
        _type = typeof(PhotonDust.ParticleSystem);
        _type = typeof(ScaleMultiplierMode);
        _type = typeof(AudioSimulator);
    }

    private async Task ShutdownEngineAsync()
    {
        var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(_appConfig.ShutdownTimeoutSeconds * 1000);

        await _worldService.StopAllWorldsAsync(tokenSource.Token);

        if (_engine.Cloud.CurrentUser is not null)
        {
            // Userspace.ExitAppが待てないのでレコードのSyncが終わってるかを確認して待つ
            await _engine.RecordManager.WaitForPendingUploadsAsync(ct: tokenSource.Token);
        }

        _engine.RequestShutdown();

        await _engine.Cloud.FinalizeSession();
    }

    private async Task EngineLoopAsync(CancellationToken ct = default)
    {
        Task? shutdownEngineTask = null;
        var isShuttingDown = false;

        while (!ct.IsCancellationRequested || !_engineShutdownComplete || !_applicationStartupComplete)
        {
            try
            {
                _engine.RunUpdateLoop();
                _systemInfo.FrameFinished();
                _engine.PerfStats.Update(_systemInfo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error during engine update loop");
            }

            await _tickTimer.WaitForNextTickAsync(CancellationToken.None);

            if (!ct.IsCancellationRequested || isShuttingDown || !_applicationStartupComplete)
            {
                continue;
            }
            isShuttingDown = true;
            shutdownEngineTask = ShutdownEngineAsync();
        }

        await (shutdownEngineTask ?? Task.CompletedTask);
    }

    private Task LoginAsync(string? credential, string? password, string? token = null) => _engine.GlobalCoroutineManager.StartTask(
        async () =>
        {
            await default(NextUpdate);

            if (string.IsNullOrWhiteSpace(credential)) return;

            LoginAuthentication auth = string.IsNullOrWhiteSpace(password) ? new SessionTokenLogin(token) : new PasswordLogin(password);
            _logger.LogInformation($"Logging in as {credential}");
            var login = await _engine.Cloud.Session.Login(
                credential,
                auth,
                _engine.LocalDB.SecretMachineID,
                false,
                null
            );

            if (login.IsOK)
            {
                _logger.LogInformation("Logged in successfull");
            }
            else
            {
                _logger.LogWarning($"Failed to log in: {login.Content}");
            }
        }
    );

    private void AllowHosts(IEnumerable<Rpc.AllowedAccessEntry> hosts) => Userspace.UserspaceWorld.RunSynchronously(
        async () =>
        {
            var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
            foreach (var host in hosts)
            {
                foreach (var port in host.Ports)
                {
                    _logger.LogInformation("Allowing host: " + host.Host + ", Port: " + port);
                    if (host.AccessTypes.Contains(Rpc.AllowedAccessEntry.Types.AccessType.Http))
                    {
                        securitySettings.AllowHTTP_Requests(host.Host, port);
                    }
                    if (host.AccessTypes.Contains(Rpc.AllowedAccessEntry.Types.AccessType.Websocket))
                    {
                        securitySettings.AllowWebsocket(host.Host, port);
                    }
                    if (host.AccessTypes.Contains(Rpc.AllowedAccessEntry.Types.AccessType.OscSending))
                    {
                        securitySettings.AllowOSC_Sending(host.Host, port);
                    }
                    if (host.Host == "localhost" && host.AccessTypes.Contains(Rpc.AllowedAccessEntry.Types.AccessType.OscReceiving))
                    {
                        securitySettings.AllowOSC_Receiving(port);
                    }
                }
            }
        }
    );
}