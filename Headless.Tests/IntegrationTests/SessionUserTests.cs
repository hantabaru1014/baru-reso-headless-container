using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Coverage for the session-user gRPC handlers:
///   ListUsersInSession / InviteUser / AllowUserToJoin / UpdateUserRole /
///   KickUser / BanUser.
///
/// The container runs in guest mode so we cannot drive real user
/// join/leave traffic, but every handler still walks the FrooxEngine
/// World/AllUsers/Permissions APIs to validate input — that's exactly
/// the surface area that breaks on SDK updates. Most tests therefore
/// drive a real session and assert the engine-side validation produces
/// the expected status code.
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
    public async Task ListUsersInSession_OnNewSession_ContainsHostUser()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var resp = await client.ListUsersInSessionAsync(new ListUsersInSessionRequest
            {
                SessionId = sessionId,
            });

            Assert.NotNull(resp);
            // The headless itself joins every running world as the host
            // user, so the user list is non-empty as soon as the session
            // is up. Catching this assertion failing is the quickest way
            // to notice AllUsers / Role / UserName property renames.
            Assert.NotEmpty(resp.Users);
            Assert.Contains(resp.Users, u => !string.IsNullOrEmpty(u.Name));
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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
    public async Task InviteUser_UnknownUserName_ReturnsInvalidArgument()
    {
        // Guest container: contacts list is empty so FindContact returns
        // null and the controller maps that to InvalidArgument. Still
        // exercises Cloud.Contacts.FindContact end-to-end.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.InviteUserAsync(new InviteUserRequest
                {
                    SessionId = sessionId,
                    UserName = "definitely-not-a-real-contact",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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
    public async Task AllowUserToJoin_OnLiveSession_DoesNotThrow()
    {
        // World.AllowUserToJoin is a fire-and-forget engine call when the
        // user isn't present yet. We expect no error to be raised.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var resp = await client.AllowUserToJoinAsync(new AllowUserToJoinRequest
            {
                SessionId = sessionId,
                UserId = "U-test-allowed",
            });
            Assert.NotNull(resp);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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
    public async Task UpdateUserRole_InvalidRoleName_ReturnsInvalidArgument()
    {
        // Role lookup happens inside the engine coroutine; the controller
        // wraps the missing-role case in an RpcException. This catches
        // renames of Permissions.Roles / RoleName API surface.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.UpdateUserRoleAsync(new UpdateUserRoleRequest
                {
                    SessionId = sessionId,
                    UserId = "U-test",
                    Role = "absolutely-not-a-real-role",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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
    public async Task KickUser_UnknownUserInSession_ReturnsInvalidArgument()
    {
        // No matching FrooxEngine.User in AllUsers → controller returns
        // InvalidArgument. Walks the AllUsers enumeration code path.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.KickUserAsync(new KickUserRequest
                {
                    SessionId = sessionId,
                    UserId = "U-not-in-session",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
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
    public async Task BanUser_UnknownUserInSession_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        var sessionId = await GrpcTestHelpers.StartGridSessionAsync(client);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.BanUserAsync(new BanUserRequest
                {
                    SessionId = sessionId,
                    UserId = "U-not-in-session",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await GrpcTestHelpers.TryStopSessionAsync(client, sessionId);
        }
    }
}
