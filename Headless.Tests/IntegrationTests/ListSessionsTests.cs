using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the ListSessions RPC endpoint.
/// </summary>
[Collection("Container")]
public class ListSessionsTests
{
    private readonly ContainerFixture _fixture;

    public ListSessionsTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListSessions_ShouldReturnResponse()
    {
        // Arrange - Wait for application startup to complete
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Engine did not become ready in time");

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        // Act
        var response = await client.ListSessionsAsync(new ListSessionsRequest());

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Sessions);
        // At startup with no worlds configured, sessions list should be empty
        // (unless StartWorldTests runs first in which case there may be a session)
    }

    [Fact]
    public async Task ListSessions_WithActiveSession_PopulatesSessionFields()
    {
        // Drives the same code path as the smoke test above but with an
        // active session so we can sanity-check the ToProto() mapping —
        // any rename of RunningSession / World properties shows up here.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await Helpers.GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await Helpers.GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var resp = await client.ListSessionsAsync(new ListSessionsRequest());
            var match = resp.Sessions.FirstOrDefault(s => s.Id == sessionId);
            Assert.NotNull(match);
            Assert.False(string.IsNullOrEmpty(match!.SessionUrl));
            Assert.NotNull(match.StartupParameters);
            Assert.NotNull(match.StartedAt);
        }
        finally
        {
            await Helpers.GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
    }
}
