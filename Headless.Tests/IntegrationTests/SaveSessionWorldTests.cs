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
    public async Task SaveSessionWorld_OnLiveSessionWithoutLogin_ReturnsResponse()
    {
        // Guest mode → ShouldSave is false, SaveWorld is a no-op, and the
        // controller returns the (empty) RecordURL. We only care that the
        // RPC completes without throwing — that means the engine save path
        // was successfully reached.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var resp = await client.SaveSessionWorldAsync(new SaveSessionWorldRequest
            {
                SessionId = sessionId,
            });
            Assert.NotNull(resp);
            // SavedWorldUrl can be empty for guest mode (no record yet),
            // but the field itself must always be set (proto string never null).
            Assert.NotNull(resp.SavedWorldUrl);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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

    // Note: a SaveAsSessionWorld happy-path test is intentionally NOT
    // included. SaveWorldCopy(ownerId: null) does run end-to-end in
    // guest mode, but the save mutates Instance.CorrespondingRecord and
    // leaves the world in a state that destabilises subsequent
    // StartWorld → StopSession cycles in the shared container fixture
    // (observed as multi-minute hangs in CI on arm64). A meaningful
    // happy path needs a logged-in fixture; see ContainerCollection's
    // coverage note.
}
