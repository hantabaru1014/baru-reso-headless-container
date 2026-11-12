using Microsoft.Extensions.Logging;

namespace Headless.Libs;

/// <summary>
/// FrooxEngine の更新ループが一定時間フレームを進めなくなったことを検知し、指定のアクション (本番ではプロセスの即時終了) を実行する。
/// </summary>
/// <remarks>
/// エンジン内部 (BepuPhysics など) の無限ループで更新スレッドが固まると、プロセスや gRPC サーバーは生きたまま
/// StartWorld 等がエンジン待ちで応答せず、graceful shutdown も完了しなくなる。外部から再起動させるために自プロセスを落とす。
/// 監視は <see cref="MarkInitialized"/> の呼び出し以降に有効になる。
/// </remarks>
public sealed class EngineHangWatchdog : IDisposable
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _timeout;
    private readonly TimeSpan _checkInterval;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _onHang;
    private readonly Func<string?>? _describeEngineState;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopCts = new();

    private long _lastProgressTimestamp;
    private int _initialized;
    private int _fired;
    private Thread? _thread;

    /// <param name="timeout">最終進行からこの時間を超えたらハングとみなす。0 以下で無効</param>
    /// <param name="onHang">ハング検知時に一度だけ呼ばれる。引数はログに出したものと同じメッセージ</param>
    /// <param name="logger">ロガー</param>
    /// <param name="timeProvider">時計。省略時は <see cref="TimeProvider.System"/></param>
    /// <param name="checkInterval">監視スレッドのチェック間隔。省略時は <see cref="DefaultCheckInterval"/></param>
    /// <param name="describeEngineState">ハング検知時にログへ添えるエンジンの状態 (ワールド名など)。例外は握りつぶす</param>
    public EngineHangWatchdog(
        TimeSpan timeout,
        Action<string> onHang,
        ILogger logger,
        TimeProvider? timeProvider = null,
        TimeSpan? checkInterval = null,
        Func<string?>? describeEngineState = null)
    {
        _timeout = timeout;
        _onHang = onHang;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _checkInterval = checkInterval ?? DefaultCheckInterval;
        _describeEngineState = describeEngineState;
        _lastProgressTimestamp = _timeProvider.GetTimestamp();
    }

    public bool IsEnabled => _timeout > TimeSpan.Zero;

    /// <summary>
    /// 更新ループの初期化 (InitializeUpdateLoop) が完了したことを通知し、監視を開始する
    /// </summary>
    public void MarkInitialized()
    {
        ReportProgress();
        Volatile.Write(ref _initialized, 1);
    }

    /// <summary>
    /// 更新ループが 1 フレーム進んだことを通知する。更新スレッドから毎フレーム呼ばれる
    /// </summary>
    public void ReportProgress()
    {
        Interlocked.Exchange(ref _lastProgressTimestamp, _timeProvider.GetTimestamp());
    }

    /// <summary>
    /// 最終進行からの経過時間を確認し、閾値を超えていればハング時のアクションを実行する
    /// </summary>
    /// <returns>このチェックでアクションを実行した場合 true</returns>
    public bool Check()
    {
        if (!IsEnabled || Volatile.Read(ref _initialized) == 0 || Volatile.Read(ref _fired) != 0)
        {
            return false;
        }

        var elapsed = _timeProvider.GetElapsedTime(Interlocked.Read(ref _lastProgressTimestamp));
        if (elapsed < _timeout)
        {
            return false;
        }

        if (Interlocked.Exchange(ref _fired, 1) != 0)
        {
            return false;
        }

        var message = $"FrooxEngine update loop has not progressed for {elapsed.TotalSeconds:F0} seconds (timeout: {_timeout.TotalSeconds:F0}s). Terminating the process.";
        var state = DescribeEngineState();
        if (state is not null)
        {
            message += $" Engine state: {state}";
        }

        _logger.LogCritical("{Message}", message);
        _onHang(message);
        return true;
    }

    /// <summary>
    /// 監視スレッドを開始する。無効化されている場合は何もしない
    /// </summary>
    public void Start()
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("Engine hang watchdog is disabled");
            return;
        }
        if (_thread is not null) return;

        // ThreadPool の枯渇などに巻き込まれないよう専用スレッドで回す
        _thread = new Thread(RunLoop)
        {
            Name = "Engine Hang Watchdog",
            IsBackground = true,
        };
        _thread.Start();
        _logger.LogInformation("Engine hang watchdog started (timeout: {Timeout}s)", _timeout.TotalSeconds);
    }

    private void RunLoop()
    {
        var token = _stopCts.Token;
        while (!token.WaitHandle.WaitOne(_checkInterval))
        {
            try
            {
                if (Check()) return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Engine hang watchdog check failed");
            }
        }
    }

    private string? DescribeEngineState()
    {
        if (_describeEngineState is null) return null;
        try
        {
            return _describeEngineState();
        }
        catch (Exception ex)
        {
            return $"(failed to describe: {ex.Message})";
        }
    }

    /// <summary>
    /// 監視を止める。更新ループが正常に終了した後に呼ぶ
    /// </summary>
    public void Dispose()
    {
        if (_stopCts.IsCancellationRequested) return;
        _stopCts.Cancel();
    }
}
