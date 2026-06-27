using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;

namespace Headless.Tests.Helpers;

/// <summary>
/// Shared helpers used by RPC integration tests. Keeps the per-file
/// boilerplate (wait for application-startup, start a Grid preset world,
/// poll for teardown) in one place so the individual test files focus on
/// what they're actually asserting.
/// </summary>
internal static class GrpcTestHelpers
{
    public static async Task<HeadlessControlService.HeadlessControlServiceClient> CreateReadyClientAsync(
        GrpcChannel channel,
        ContainerFixture fixture)
    {
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Application startup did not complete in time");
        return new HeadlessControlService.HeadlessControlServiceClient(channel);
    }

    public static async Task<string> StartGridSessionAsync(
        HeadlessControlService.HeadlessControlServiceClient client)
    {
        var response = await client.StartWorldAsync(new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private,
            }
        });
        Assert.NotNull(response.OpenedSession);
        Assert.False(string.IsNullOrEmpty(response.OpenedSession.Id));
        // Userspace.OpenWorld returns as soon as the world leaves the
        // Initializing state, but several downstream code paths
        // (AttachEventBus, RunningSession property setters) expect a
        // couple of engine ticks before being read back. Match the wait
        // the other test files use for consistency.
        await Task.Delay(TimeSpan.FromSeconds(5));
        return response.OpenedSession.Id;
    }

    public static async Task TryStopSessionAsync(
        HeadlessControlService.HeadlessControlServiceClient client,
        string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        try
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
