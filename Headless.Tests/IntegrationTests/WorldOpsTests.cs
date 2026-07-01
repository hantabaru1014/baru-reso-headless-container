using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the world-manipulation gRPC handlers:
///   SendDynamicImpulse / RunGarbageCollection / GetWorldDebugState.
///
/// The container runs in guest mode without a live session for most
/// tests here — we're mostly asserting the InvalidArgument path on
/// missing/unknown session_id, plus that RunGarbageCollection (which
/// does not require a session) always succeeds.
/// </summary>
[Collection("Container")]
public class WorldOpsTests
{
    private readonly ContainerFixture _fixture;

    public WorldOpsTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunGarbageCollection_Succeeds()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        // gc は session を要求しないため、単に返れば OK。
        var response = await client.RunGarbageCollectionAsync(new RunGarbageCollectionRequest());
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SendDynamicImpulse_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.SendDynamicImpulseAsync(new SendDynamicImpulseRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                Tag = "TestImpulse",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task SendDynamicImpulse_EmptyTag_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        // Session が存在しない側で先に落ちる可能性はあるが、いずれにせよ
        // InvalidArgument になることを保証する。
        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.SendDynamicImpulseAsync(new SendDynamicImpulseRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                Tag = "",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetWorldDebugState_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetWorldDebugStateAsync(new GetWorldDebugStateRequest
            {
                SessionId = "S-U-does-not-exist:nope",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }
}
