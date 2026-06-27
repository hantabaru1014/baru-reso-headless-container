using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the cloud-side RPC handlers when the container is running
/// in guest mode (no login). These tests do not assert "useful" payloads —
/// they cannot, since there is no signed-in user — but they DO drive the
/// real Cloud / Contacts / Messages / RecordManager SDK code paths. If
/// the upstream SDK renames Contact.ContactUsername, Cloud.Storage.CurrentStorage,
/// RecordManager.FetchRecord etc., these tests fail.
/// </summary>
[Collection("Container")]
public class CloudRpcTests
{
    private readonly ContainerFixture _fixture;

    public CloudRpcTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAccountInfo_GuestMode_ReturnsNotFound()
    {
        // The controller maps "no CurrentUser" to NotFound. Asserting that
        // gives us a signal both that Cloud.CurrentUser is still the
        // discriminator AND that the controller still surfaces it.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetAccountInfoAsync(new GetAccountInfoRequest());
        });
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetFriendRequests_GuestMode_ReturnsEmpty()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var resp = await client.GetFriendRequestsAsync(new GetFriendRequestsRequest());
        Assert.NotNull(resp);
        Assert.Empty(resp.Users);
    }

    [Fact]
    public async Task ListContacts_GuestMode_ReturnsEmptyWithNoCursor()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var resp = await client.ListContactsAsync(new ListContactsRequest { Limit = 50 });
        Assert.NotNull(resp);
        Assert.Empty(resp.Users);
        // When contacts < limit there should be no next cursor.
        Assert.True(string.IsNullOrEmpty(resp.NextCursor));
    }

    [Fact]
    public async Task SearchUserInfo_EmptyUserName_ReturnsEmpty()
    {
        // The controller short-circuits the empty input to an empty
        // response without touching the cloud. This exercises the input
        // validation branch.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var resp = await client.SearchUserInfoAsync(new SearchUserInfoRequest
        {
            UserName = "",
            OnlyInContacts = true,
        });
        Assert.NotNull(resp);
        Assert.Empty(resp.Users);
    }

    [Fact]
    public async Task SearchUserInfo_OnlyInContactsForGuest_ReturnsEmpty()
    {
        // Walks Cloud.Contacts.ForeachContact — empty in guest mode.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var resp = await client.SearchUserInfoAsync(new SearchUserInfoRequest
        {
            UserName = "anyone",
            OnlyInContacts = true,
        });
        Assert.NotNull(resp);
        Assert.Empty(resp.Users);
    }

    [Fact]
    public async Task AcceptFriendRequests_EmptyList_DoesNotThrow()
    {
        // Zero-item path is a no-op; verify we still get a clean response
        // back. Catches "Cloud.Contacts.GetContact panics on missing id"
        // style regressions in the SDK (we don't even loop here).
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var resp = await client.AcceptFriendRequestsAsync(new AcceptFriendRequestsRequest());
        Assert.NotNull(resp);
    }

    // Note: FetchWorldInfo / GetContactMessages / SendContactMessage are
    // intentionally NOT covered here. In guest mode they each fail with
    // a non-deterministic status code (Internal when the SDK returns an
    // error result, Unknown when it throws an NRE on the null current
    // user). The only assertion we could make — "throws SOME RpcException"
    // — is meaningless: every unhandled handler exception is wrapped as
    // RpcException(Unknown) by the gRPC pipeline, so the test would
    // green even if the underlying SDK method was renamed or removed.
    // A meaningful test for these RPCs needs a logged-in fixture; see
    // ContainerCollection's coverage note.
}
