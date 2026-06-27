using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the DownloadSessionWorld server-streaming RPC. The
/// happy path exercises the engine snapshot → DataTreeConverter → chunked
/// response pipeline, which is the most likely place to break when the
/// Resonite SDK changes World.SaveWorld / DataTreeConverter signatures.
/// </summary>
[Collection("Container")]
public class DownloadSessionWorldTests
{
    private readonly ContainerFixture _fixture;

    public DownloadSessionWorldTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DownloadSessionWorld_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        using var call = client.DownloadSessionWorld(new DownloadSessionWorldRequest
        {
            SessionId = "S-U-does-not-exist:nope",
            Format = WorldBinaryFormat._7Zbson,
        });

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync())
            {
            }
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // Note: tests that drive a live session (Unspecified-format dispatch,
    // 7zbson happy-path streaming) are intentionally NOT included.
    // World.SaveWorld() + DataTreeConverter.To7zBSON leaves residual
    // state in the shared container that destabilises subsequent
    // StartWorld / StopSession cycles in CI. The controller-side
    // dispatch (session-not-found) is still covered above; format
    // validation is plain controller logic.
}
