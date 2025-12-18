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
    /// Waits for the "Application startup complete" log message.
    /// This indicates that Engine, Userspace, login, and initial world setup are all complete.
    /// </summary>
    /// <param name="getLogsFunc">Function to retrieve logs</param>
    /// <param name="timeout">Maximum time to wait (default: 5 minutes)</param>
    /// <returns>True if application startup is complete, false if timeout was reached</returns>
    public static Task<bool> WaitForApplicationStartupAsync(
        Func<Task<string>> getLogsFunc,
        TimeSpan? timeout = null)
    {
        const string pattern = "Application startup complete";
        return WaitForLogPatternAsync(
            getLogsFunc,
            pattern,
            timeout ?? TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(2));
    }
}
