using System.Reflection;
using FrooxEngine;
using Grpc.Core;
using Headless.Events;
using Headless.Libs;
using Headless.Rpc;

namespace Headless.Services;

public partial class GrpcControllerService : HeadlessControlService.HeadlessControlServiceBase
{
    private readonly Engine _engine;
    private readonly WorldService _worldService;
    private readonly IFrooxEngineRunnerService _runnerService;
    private readonly HostEventBus _eventBus;
    private readonly ILogger<GrpcControllerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _appVersion;

    private bool _isShutdownRequested = false;

    public GrpcControllerService
    (
        Engine engine,
        WorldService worldService,
        IFrooxEngineRunnerService runnerService,
        HostEventBus eventBus,
        ILogger<GrpcControllerService> logger,
        ILoggerFactory loggerFactory
    )
    {
        _engine = engine;
        _worldService = worldService;
        _runnerService = runnerService;
        _eventBus = eventBus;
        _logger = logger;
        _loggerFactory = loggerFactory;

        CloudUtils.Setup(_engine.Cloud.Assets);

        _appVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
    }

    public override Task<GetAboutResponse> GetAbout(GetAboutRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetAboutResponse
        {
            AppVersion = _appVersion,
            ResoniteVersion = _engine.VersionString,
        });
    }

    public override Task<GetStatusResponse> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetStatusResponse
        {
            Fps = ((SystemInfo)_engine.SystemInfo).FPS, // TODO: もっといい感じにする
            TotalEngineUpdateTime = _engine.TotalEngineUpdateTime,
            SyncingRecordsCount = _engine.RecordManager.SyncingRecordsCount,
        });
    }

    public override Task<ShutdownResponse> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        if (!_isShutdownRequested)
        {
            _isShutdownRequested = true;

            // OnShutdownRequest をフックにしてStandaloneFrooxEngineServiceのctをキャンセルするが、
            // そのままRequestShutdownをしてしまうとEngineの更新が止まって保存したワールドのアップロードが開始しないので一回だけCancelShutdownしておく
            _engine.CancelShutdown();
            _engine.RequestShutdown();
        }
        return Task.FromResult(new ShutdownResponse());
    }
}
