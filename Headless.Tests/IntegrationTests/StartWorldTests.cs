using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the StartWorld RPC endpoint.
/// </summary>
[Collection("Container")]
public class StartWorldTests
{
    private readonly ContainerFixture _fixture;

    public StartWorldTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartWorld_WithGridPreset_ShouldIncreaseSessionCount()
    {
        // Arrange - Wait for engine to be ready
        var ready = await LogPollingHelper.WaitForEngineReadyAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Engine did not become ready in time");

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        // Get initial session count
        var initialResponse = await client.ListSessionsAsync(new ListSessionsRequest());
        var initialCount = initialResponse.Sessions.Count;

        // Act - Start a world with Grid preset
        var startRequest = new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private
            }
        };

        var startResponse = await client.StartWorldAsync(startRequest);

        // Assert
        Assert.NotNull(startResponse);
        Assert.NotNull(startResponse.OpenedSession);
        Assert.NotEmpty(startResponse.OpenedSession.Id);

        // Wait a bit for the session to be fully initialized
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Verify session count increased
        var finalResponse = await client.ListSessionsAsync(new ListSessionsRequest());
        Assert.Equal(initialCount + 1, finalResponse.Sessions.Count);

        // Verify the started session is in the list
        var foundSession = finalResponse.Sessions
            .FirstOrDefault(s => s.Id == startResponse.OpenedSession.Id);
        Assert.NotNull(foundSession);
    }
}
