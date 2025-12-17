namespace Headless.Tests.Helpers;

public static class LogPollingHelper
{
    /// <summary>
    /// Polls logs until the specified pattern is found or timeout is reached.
    /// </summary>
    /// <param name="getLogsFunc">Function to retrieve logs</param>
    /// <param name="pattern">Pattern to search for in logs</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="pollInterval">Interval between polling attempts</param>
    /// <returns>True if pattern was found, false if timeout was reached</returns>
    public static async Task<bool> WaitForLogPatternAsync(
        Func<Task<string>> getLogsFunc,
        string pattern,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var logs = await getLogsFunc();
            if (logs.Contains(pattern))
            {
                return true;
            }
            await Task.Delay(pollInterval);
        }

        return false;
    }

    /// <summary>
    /// Waits for the "Engine Ready!" log message from FrooxEngineRunnerService.
    /// </summary>
    /// <param name="getLogsFunc">Function to retrieve logs</param>
    /// <param name="timeout">Maximum time to wait (default: 5 minutes)</param>
    /// <returns>True if engine is ready, false if timeout was reached</returns>
    public static Task<bool> WaitForEngineReadyAsync(
        Func<Task<string>> getLogsFunc,
        TimeSpan? timeout = null)
    {
        const string pattern = "Engine Ready!";
        return WaitForLogPatternAsync(
            getLogsFunc,
            pattern,
            timeout ?? TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(2));
    }
}
