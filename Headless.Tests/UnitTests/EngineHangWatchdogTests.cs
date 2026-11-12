using Headless.Libs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Tests.UnitTests;

public class EngineHangWatchdogTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(300);

    private readonly ManualTimeProvider _time = new();
    private readonly List<string> _hangMessages = new();

    private EngineHangWatchdog CreateWatchdog(TimeSpan timeout, Func<string?>? describe = null) =>
        new(timeout, _hangMessages.Add, NullLogger.Instance, _time, describeEngineState: describe);

    [Fact]
    public void Check_FiresWhenNoProgressBeyondTimeout()
    {
        using var watchdog = CreateWatchdog(Timeout);
        watchdog.MarkInitialized();

        _time.Advance(Timeout - TimeSpan.FromSeconds(1));
        Assert.False(watchdog.Check());

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(watchdog.Check());

        var message = Assert.Single(_hangMessages);
        Assert.Contains("300 seconds", message);
    }

    [Fact]
    public void Check_FiresOnlyOnce()
    {
        using var watchdog = CreateWatchdog(Timeout);
        watchdog.MarkInitialized();

        _time.Advance(Timeout);
        Assert.True(watchdog.Check());
        _time.Advance(Timeout);
        Assert.False(watchdog.Check());

        Assert.Single(_hangMessages);
    }

    [Fact]
    public void Check_DoesNotFireWhileFramesProgress()
    {
        using var watchdog = CreateWatchdog(Timeout);
        watchdog.MarkInitialized();

        for (var i = 0; i < 10; i++)
        {
            _time.Advance(Timeout - TimeSpan.FromSeconds(1));
            watchdog.ReportProgress();
            Assert.False(watchdog.Check());
        }

        Assert.Empty(_hangMessages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Check_DoesNotFireWhenDisabled(int timeoutSeconds)
    {
        using var watchdog = CreateWatchdog(TimeSpan.FromSeconds(timeoutSeconds));
        watchdog.MarkInitialized();

        _time.Advance(TimeSpan.FromDays(1));

        Assert.False(watchdog.IsEnabled);
        Assert.False(watchdog.Check());
        Assert.Empty(_hangMessages);
    }

    [Fact]
    public void Check_DoesNotFireBeforeInitialized()
    {
        using var watchdog = CreateWatchdog(Timeout);

        _time.Advance(TimeSpan.FromDays(1));
        Assert.False(watchdog.Check());
        Assert.Empty(_hangMessages);
    }

    [Fact]
    public void MarkInitialized_StartsMeasuringFromInitialization()
    {
        using var watchdog = CreateWatchdog(Timeout);

        // 初期化 (InitializeUpdateLoop) に時間がかかっても、その時間はハング扱いしない
        _time.Advance(TimeSpan.FromDays(1));
        watchdog.MarkInitialized();
        _time.Advance(Timeout - TimeSpan.FromSeconds(1));

        Assert.False(watchdog.Check());
    }

    [Fact]
    public void Check_IncludesEngineStateInMessage()
    {
        using var watchdog = CreateWatchdog(Timeout, () => "World A (Stage: Changes)");
        watchdog.MarkInitialized();

        _time.Advance(Timeout);
        Assert.True(watchdog.Check());

        Assert.Contains("World A (Stage: Changes)", Assert.Single(_hangMessages));
    }

    [Fact]
    public void Check_FiresEvenIfDescribingEngineStateThrows()
    {
        using var watchdog = CreateWatchdog(Timeout, () => throw new InvalidOperationException("boom"));
        watchdog.MarkInitialized();

        _time.Advance(Timeout);
        Assert.True(watchdog.Check());

        Assert.Contains("boom", Assert.Single(_hangMessages));
    }

    [Fact]
    public async Task Start_MonitorThreadFiresOnHang()
    {
        var fired = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watchdog = new EngineHangWatchdog(
            TimeSpan.FromMilliseconds(200),
            msg => fired.TrySetResult(msg),
            NullLogger.Instance,
            checkInterval: TimeSpan.FromMilliseconds(20));
        watchdog.MarkInitialized();
        watchdog.Start();

        var message = await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("has not progressed", message);
    }

    [Fact]
    public async Task Dispose_StopsMonitorThread()
    {
        var fired = false;
        var watchdog = new EngineHangWatchdog(
            TimeSpan.FromMilliseconds(300),
            _ => Volatile.Write(ref fired, true),
            NullLogger.Instance,
            checkInterval: TimeSpan.FromMilliseconds(20));
        watchdog.MarkInitialized();
        watchdog.Start();
        watchdog.Dispose();

        await Task.Delay(1000);
        Assert.False(Volatile.Read(ref fired));
    }
}
