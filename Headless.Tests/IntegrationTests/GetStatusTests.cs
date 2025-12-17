using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the GetStatus RPC endpoint.
/// </summary>
[Collection("Container")]
public class GetStatusTests
{
    private readonly ContainerFixture _fixture;

    public GetStatusTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetStatus_ShouldReturnValidResponse()
    {
        // Arrange - Wait for engine to be ready
        var ready = await LogPollingHelper.WaitForEngineReadyAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Engine did not become ready in time");

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        // Act
        var response = await client.GetStatusAsync(new GetStatusRequest());

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Fps >= 0, "FPS should be non-negative");
        Assert.True(response.TotalEngineUpdateTime >= 0,
            "TotalEngineUpdateTime should be non-negative");
        Assert.True(response.SyncingRecordsCount >= 0,
            "SyncingRecordsCount should be non-negative");
    }
}
