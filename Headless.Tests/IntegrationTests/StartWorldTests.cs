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
        // Arrange - Wait for application startup to complete
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Application startup did not complete in time");

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        // Get initial session count
        var initialResponse = await client.ListSessionsAsync(new ListSessionsRequest());
        var initialCount = initialResponse.Sessions.Count;

        // Act - Start a world with Grid preset
        const uint lnlPort = 45678;
        const uint quicPort = 45679;
        var startRequest = new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private,
                ForcePorts =
                {
                    new ForcePort { Protocol = NetworkProtocol.Lnl, Port = lnlPort },
                    new ForcePort { Protocol = NetworkProtocol.Quic, Port = quicPort },
                }
            }
        };

        try
        {
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

            // Verify force_ports survives the proto -> engine -> proto round trip
            var startupParameters = foundSession.StartupParameters;
            Assert.NotNull(startupParameters);
            var ports = startupParameters.ForcePorts.ToDictionary(p => p.Protocol, p => p.Port);
            Assert.Equal(lnlPort, ports[NetworkProtocol.Lnl]);
            Assert.Equal(quicPort, ports[NetworkProtocol.Quic]);
#pragma warning disable CS0612 // legacy な force_port は LNL のミラーであり続ける
            Assert.Equal(lnlPort, startupParameters.ForcePort);
#pragma warning restore CS0612
        }
        catch (Exception ex)
        {
            // Output container logs for debugging
            var logs = await _fixture.GetLogsAsync();
            throw new Exception($"StartWorld failed. Container logs:\n{logs}", ex);
        }
    }
}
