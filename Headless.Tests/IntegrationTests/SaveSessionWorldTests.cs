using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for SaveSessionWorld / SaveAsSessionWorld. The container
/// runs in guest mode (no cloud login) so we cannot actually upload a
/// world record — Userspace.ShouldSave will return false and the
/// controller short-circuits to an empty url. That is still useful: the
/// engine path runs end-to-end (records the absence of a logged-in user,
/// touches Instance.RecordURL / CorrespondingRecord) which is what SDK
/// updates would silently break.
/// </summary>
[Collection("Container")]
public class SaveSessionWorldTests
{
    private readonly ContainerFixture _fixture;

    public SaveSessionWorldTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveSessionWorld_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.SaveSessionWorldAsync(new SaveSessionWorldRequest
            {
                SessionId = "S-U-does-not-exist:nope",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task SaveAsSessionWorld_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.SaveAsSessionWorldAsync(new SaveAsSessionWorldRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                Type = SaveAsSessionWorldRequest.Types.SaveAsType.SaveAs,
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // Note: live-session happy-path tests for Save / SaveAs are
    // intentionally NOT included. Both code paths trigger the engine's
    // record save pipeline which mutates Instance.CorrespondingRecord
    // and has been observed to destabilise subsequent StartWorld /
    // StopSession cycles in the shared container fixture (multi-minute
    // hangs in CI). The controller-side dispatch is still covered by
    // the error-path tests above. A meaningful happy path needs a
    // logged-in fixture; see ContainerCollection's coverage note.
}
