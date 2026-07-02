using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the session-user gRPC handlers:
///   ListUsersInSession / InviteUser / AllowUserToJoin / UpdateUserRole /
///   KickUser / BanUser / RespawnUser.
///
/// The container runs in guest mode so we cannot drive real user
/// join/leave traffic, but every handler still walks the FrooxEngine
/// World/AllUsers/Permissions APIs to validate input — that's exactly
/// the surface area that breaks on SDK updates.
///
/// To keep CI stable, all "needs a live session" assertions are
/// consolidated into a single test that drives one shared session
/// through every SessionUser RPC sequentially. Starting many short
/// Grid sessions in a single shared container fixture has been observed
/// to destabilise downstream tests in CI.
/// </summary>
[Collection("Container")]
public class SessionUserTests
{
    private readonly ContainerFixture _fixture;

    public SessionUserTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ListUsersInSession_NonExistentId_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.ListUsersInSessionAsync(new ListUsersInSessionRequest
            {
                SessionId = "S-U-does-not-exist:nope",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task InviteUser_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.InviteUserAsync(new InviteUserRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task AllowUserToJoin_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.AllowUserToJoinAsync(new AllowUserToJoinRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRole_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.UpdateUserRoleAsync(new UpdateUserRoleRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
                Role = "Admin",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task KickUser_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.KickUserAsync(new KickUserRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task BanUser_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.BanUserAsync(new BanUserRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task RespawnUser_NonExistentSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.RespawnUserAsync(new RespawnUserRequest
            {
                SessionId = "S-U-does-not-exist:nope",
                UserId = "U-1",
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task SessionUserRpcs_AgainstLiveSession_ExerciseEngineValidation()
    {
        // Single shared session is reused for every "needs a live session"
        // assertion below. Starting one Grid world per assertion would
        // multiply test runtime by ~6 and has been observed to destabilise
        // downstream tests in CI.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            // AllowUserToJoin: fire-and-forget engine call when the user
            // is not present yet — should not throw.
            var allowResp = await client.AllowUserToJoinAsync(new AllowUserToJoinRequest
            {
                SessionId = sessionId,
                UserId = "U-test-allowed",
            });
            Assert.NotNull(allowResp);

            // (InviteUser-by-username is intentionally NOT asserted here:
            // in guest mode FindContact returns null and the controller
            // dereferences it, yielding RpcException(Unknown). Asserting
            // "any RpcException" would also accept arbitrary handler
            // crashes, so the test would green even if the SDK removed
            // FindContact entirely. Needs a logged-in fixture to be
            // meaningful.)

            // UpdateUserRole with an invalid role name: the engine coroutine
            // looks up Permissions.Roles and returns InvalidArgument when
            // the role is missing.
            var roleEx = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.UpdateUserRoleAsync(new UpdateUserRoleRequest
                {
                    SessionId = sessionId,
                    UserId = "U-test",
                    Role = "absolutely-not-a-real-role",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, roleEx.StatusCode);

            // KickUser / BanUser / RespawnUser with an unknown user: walks AllUsers.
            var kickEx = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.KickUserAsync(new KickUserRequest
                {
                    SessionId = sessionId,
                    UserId = "U-not-in-session",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, kickEx.StatusCode);

            var banEx = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.BanUserAsync(new BanUserRequest
                {
                    SessionId = sessionId,
                    UserId = "U-not-in-session",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, banEx.StatusCode);

            var respawnEx = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.RespawnUserAsync(new RespawnUserRequest
                {
                    SessionId = sessionId,
                    UserId = "U-not-in-session",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, respawnEx.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
    }

    // Note: a "ListUsersInSession returns the host user" happy-path test
    // is intentionally NOT included. In guest mode the headless's host
    // FrooxEngine.User has a null UserID, which makes the controller
    // throw an NRE when constructing the UserInSession proto. A
    // meaningful happy path requires a logged-in fixture; see
    // ContainerCollection's coverage note.
}
