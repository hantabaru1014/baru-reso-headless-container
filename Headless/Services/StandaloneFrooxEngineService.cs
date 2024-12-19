using System.Reflection;
using System.Text.Json;
using FrooxEngine;
using Headless.Configuration;
using Headless.Extensions;
using Microsoft.Extensions.Options;
using SkyFrost.Base;

namespace Headless.Services;

public class StandaloneFrooxEngineService : BackgroundService
{
    private static Type? _type;

    private readonly ILogger<StandaloneFrooxEngineService> _logger;
    private readonly ApplicationConfig _appConfig;
    private readonly Engine _engine;
    private readonly SystemInfo _systemInfo;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly WorldService _worldService;
    private readonly IConfigService _configService;

    private bool _applicationStartupComplete;
    private bool _engineShutdownComplete;

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

    public StandaloneFrooxEngineService
    (
        ILogger<StandaloneFrooxEngineService> logger,
        IOptions<ApplicationConfig> applicationConfig,
        Engine engine,
        SystemInfo systemInfo,
        IHostApplicationLifetime applicationLifetime,
        WorldService worldService,
        IConfigService configService
    )
    {
        _logger = logger;
        _appConfig = applicationConfig.Value;
        _engine = engine;
        _systemInfo = systemInfo;
        _applicationLifetime = applicationLifetime;
        _worldService = worldService;
        _configService = configService;
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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        LoadTypes();

        _logger.LogInformation(JsonSerializer.Serialize(_configService.Config));

        _engine.UsernameOverride = _configService.Config.UsernameOverride;
        _engine.EnvironmentShutdownCallback = () => _engineShutdownComplete = true;
        var launchOptions = new LaunchOptions
        {
            DataDirectory = Path.Combine(_appConfig.DataDirectoryPath, "Data"),
            CacheDirectory = Path.Combine(_appConfig.DataDirectoryPath, "Cache"),
            LogsDirectory = null,
            VerboseInit = true,
            NeverSaveSettings = true,
            NeverSaveDash = true,
            BackgroundWorkerCount = _appConfig.BackgroundWorkers ?? _configService.Config.BackgroundWorkers,
            PriorityWorkerCount = _appConfig.PriorityWorkers ?? _configService.Config.PriorityWorkers,
        };

        await _engine.Initialize(
            AppDomain.CurrentDomain.BaseDirectory,
            launchOptions,
            _systemInfo,
            null,
            new EngineInitProgressLogger(_logger)
        );

        var userspcaeWorld = Userspace.SetupUserspace(_engine);
        var engineLoop = EngineLoopAsync(ct);

        await userspcaeWorld.Coroutines.StartTask(async () => await default(ToWorld)).ConfigureAwait(false);

        if (_configService.Config.UniverseID is not null)
        {
            Engine.Config.UniverseId = _configService.Config.UniverseID;
        }

        await LoginAsync(_appConfig.HeadlessUserCredential, _appConfig.HeadlessUserPassword);
        await AllowHosts(_configService.Config.AllowedUrlHosts ?? Enumerable.Empty<string>());

        SessionAssetTransferer.OverrideMaxConcurrentTransfers = _configService.Config.MaxConcurrentAssetTransfers;
        var startWorlds = _configService.Config.StartWorlds ?? Array.Empty<WorldStartupParameters>();
        foreach (var world in startWorlds)
        {
            if (!world.IsEnabled) continue;

            _logger.LogInformation($"Starting world : {world.SessionName ?? "NoName"} ({world.LoadWorldURL})");
            await _worldService.StartWorldAsync(world, ct);
        }
        _configService.SaveConfig();

        _applicationStartupComplete = true;
        await engineLoop;
        _applicationLifetime.StopApplication();
    }

    private static void LoadTypes()
    {
        _type = typeof(ProtoFlux.Nodes.Core.Configuration);
        _type = typeof(FrooxEngine.ProtoFlux.ProtoFluxMapper);
        _type = typeof(FrooxEngine.ProtoFlux.Runtimes.Execution.VoidNode<>);
        _type = typeof(ProtoFlux.Runtimes.Execution.Nodes.Math.TangentPointFloat);
        _type = typeof(FrooxEngine.Store.Record);
    }

    private async Task ShutdownEngineAsync()
    {
        var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(_appConfig.ShutdownTimeoutSeconds * 1000);

        await _worldService.StopAllWorldsAsync(tokenSource.Token);

        // TODO: Userspace.ExitAppはGUI前提の無駄な処理してるし、待てないので本当は自前でやりたい
        Userspace.ExitApp(saveHomes: false);

        if (_engine.Cloud.CurrentUser is not null)
        {
            // Userspace.ExitAppが待てないのでレコードのSyncが終わってるかを確認して待つ
            await _engine.RecordManager.WaitForPendingUploadsAsync(ct: tokenSource.Token);
        }
    }

    private async Task EngineLoopAsync(CancellationToken ct = default)
    {
        var audioStartTime = DateTimeOffset.UtcNow;
        var audioTime = 0.0;
        var audioTickRate = 1.0 / _configService.Config.TickRate;

        using var tickTimer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / _configService.Config.TickRate));

        Task? shutdownEngineTask = null;
        var isShuttingDown = false;

        while (!ct.IsCancellationRequested || !_engineShutdownComplete || !_applicationStartupComplete)
        {
            await tickTimer.WaitForNextTickAsync(CancellationToken.None);

            try
            {
                _engine.RunUpdateLoop();
                _systemInfo.FrameFinished();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unexpected error during engine update loop");
            }

            audioTime += audioTickRate * 48000f;
            if (audioTime >= 1024.0)
            {
                audioTime = (audioTime - 1024.0) % 1024.0;
                DummyAudioConnector.UpdateCallback((DateTimeOffset.UtcNow - audioStartTime).TotalMilliseconds * 1000);
            }

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

    private Task AllowHosts(IEnumerable<string> hosts) => _engine.GlobalCoroutineManager.StartTask(
        async () =>
        {
            await default(NextUpdate);

            foreach (string host in hosts)
            {
                string _host = host.Trim().ToLower();
                int _port = 80;
                if (Uri.TryCreate(_host, UriKind.Absolute, out var url) && !string.IsNullOrEmpty(url.Host))
                {
                    _host = url.Host;
                    _port = url.Port;
                }
                else
                {
                    string[] segments = _host.Split(':');
                    switch (segments.Length)
                    {
                        case 1:
                            _host = segments[0];
                            break;
                        case 2:
                            _host = segments[0];
                            if (segments.Length > 1 && int.TryParse(segments[1], out var port))
                            {
                                _port = port;
                            }
                            break;
                    }
                }
                if (string.IsNullOrEmpty(_host))
                {
                    _logger.LogWarning($"Unable to parse allowed host entry: \"{_host}\"");
                    continue;
                }
                _logger.LogInformation("Allowing host: " + _host + ", Port: " + _port);
                _engine.Security.TemporarilyAllowHTTP(_host);
                _engine.Security.TemporarilyAllowWebsocket(_host, _port);
                _engine.Security.TemporarilyAllowOSC_Sender(_host, _port);
                if (_host == "localhost")
                {
                    _engine.Security.TemporarilyAllowOSC_Receiver(_port);
                }
            }
        }
    );
}