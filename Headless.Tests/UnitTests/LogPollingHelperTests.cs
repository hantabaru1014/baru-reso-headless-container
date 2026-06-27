using System.Diagnostics;
using Headless.Tests.Helpers;

namespace Headless.Tests.UnitTests;

/// <summary>
/// <see cref="LogPollingHelper"/> is the trip-wire that every integration
/// test in this project relies on to know when the Resonite engine has
/// finished booting. A regression here (e.g. early-return on missing
/// pattern) cascades into every container-backed test, so the helper
/// itself deserves a unit safety net.
/// </summary>
public class LogPollingHelperTests
{
    [Fact]
    public async Task WaitForLogPatternAsync_PatternPresentImmediately_ReturnsTrue()
    {
        var found = await LogPollingHelper.WaitForLogPatternAsync(
            getLogsFunc: () => Task.FromResult("ready!\nfoo"),
            pattern: "ready!",
            timeout: TimeSpan.FromSeconds(1),
            pollInterval: TimeSpan.FromMilliseconds(50));
        Assert.True(found);
    }

    [Fact]
    public async Task WaitForLogPatternAsync_PatternAppearsAfterFewPolls_ReturnsTrue()
    {
        var attempts = 0;
        var found = await LogPollingHelper.WaitForLogPatternAsync(
            getLogsFunc: () =>
            {
                attempts++;
                return Task.FromResult(attempts >= 3 ? "Application startup complete" : "starting...");
            },
            pattern: "Application startup complete",
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));
        Assert.True(found);
        Assert.True(attempts >= 3, $"Expected pattern to be hit on the 3rd poll, attempts={attempts}");
    }

    [Fact]
    public async Task WaitForLogPatternAsync_Timeout_ReturnsFalse()
    {
        var sw = Stopwatch.StartNew();
        var found = await LogPollingHelper.WaitForLogPatternAsync(
            getLogsFunc: () => Task.FromResult("never matches"),
            pattern: "MISSING",
            timeout: TimeSpan.FromMilliseconds(150),
            pollInterval: TimeSpan.FromMilliseconds(20));
        sw.Stop();

        Assert.False(found);
        // We allow some slack; the only guarantee we want is "we did wait
        // at least roughly the timeout before giving up" (not a fast-path bail).
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(120),
            $"WaitForLogPatternAsync returned too quickly: {sw.Elapsed}");
    }

    [Fact]
    public async Task WaitForApplicationStartupAsync_MatchesTheDocumentedReadyString()
    {
        // The exact string "Application startup complete" is contractual
        // — Resonite prints it once the engine + userspace + initial worlds
        // are up. If the helper changes the pattern, every integration test
        // breaks silently (timeouts), so we lock the pattern here.
        var found = await LogPollingHelper.WaitForApplicationStartupAsync(
            getLogsFunc: () => Task.FromResult("[12:00:00] Application startup complete\n"),
            timeout: TimeSpan.FromMilliseconds(500));
        Assert.True(found);
    }

    [Fact]
    public async Task WaitForApplicationStartupAsync_TimesOutWhenPatternMissing()
    {
        var found = await LogPollingHelper.WaitForApplicationStartupAsync(
            getLogsFunc: () => Task.FromResult("engine starting..."),
            timeout: TimeSpan.FromMilliseconds(150));
        Assert.False(found);
    }
}
