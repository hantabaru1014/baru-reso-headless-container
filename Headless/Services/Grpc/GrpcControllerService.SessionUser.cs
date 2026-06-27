using FrooxEngine;
using Grpc.Core;
using Headless.Rpc;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override async Task<InviteUserResponse> InviteUser(InviteUserRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        string? userId = null;
        if (request.HasUserId)
        {
            userId = request.UserId;
        }
        else if (request.HasUserName)
        {
            userId = _engine.Cloud.Contacts.FindContact(c => c.ContactUsername.Equals(request.UserName, StringComparison.InvariantCultureIgnoreCase)).ContactUserId;
        }
        if (userId is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require valid user_id or user_name"));
        }
        if (!await session.InviteUser(userId))
        {
            throw new RpcException(new Status(StatusCode.Internal, "Error sending invite!"));
        }
        return new InviteUserResponse();
    }

    public override async Task<AllowUserToJoinResponse> AllowUserToJoin(AllowUserToJoinRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        await session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            session.AllowUserToJoin(request.UserId);
        });

        return new AllowUserToJoinResponse();
    }

    public override async Task<UpdateUserRoleResponse> UpdateUserRole(UpdateUserRoleRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        // Permissions.Roles / AllUsers の参照と user.Role の書き込みを engine update スレッドにまとめる。
        // gRPC スレッドから直接読むと iter 中の collection mutation で例外を踏みうる。
        var updated = await session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);

            var permissionSet = session.Instance.Permissions.Roles.FirstOrDefault(r => r.RoleName.Value.Equals(request.Role, StringComparison.InvariantCultureIgnoreCase));
            if (permissionSet is null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid role name"));
            }
            if (permissionSet > session.Instance.HostUser.Role)
            {
                permissionSet = session.Instance.HostUser.Role;
            }

            FrooxEngine.User? user = null;
            if (request.HasUserId)
            {
                user = session.Instance.AllUsers.Where(u => !u.IsHost).FirstOrDefault(u => u.UserID == request.UserId);
            }
            else if (request.HasUserName)
            {
                user = session.Instance.AllUsers.Where(u => !u.IsHost).FirstOrDefault(u => u.UserName == request.UserName);
            }
            if (user is null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The user does not appear to be in a session!"));
            }

            user.Role = permissionSet;
            session.Instance.Permissions.AssignDefaultRole(user, permissionSet);
            return user.Role;
        });

        return new UpdateUserRoleResponse
        {
            Role = updated.RoleName.Value
        };
    }

    public override async Task<ListUsersInSessionResponse> ListUsersInSession(ListUsersInSessionRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        var users = session.Instance.AllUsers.Select(user => new Rpc.UserInSession
        {
            Id = user.UserID,
            Name = user.UserName,
            Role = user.Role.RoleName.Value,
            IsPresent = user.IsPresent
        });
        await Task.CompletedTask;
        return new ListUsersInSessionResponse
        {
            Users = { users }
        };
    }

    public override Task<KickUserResponse> KickUser(KickUserRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        FrooxEngine.User? user = null;
        if (request.HasUserId)
        {
            user = session.Instance.AllUsers.FirstOrDefault(u => u.UserID == request.UserId);
        }
        else if (request.HasUserName)
        {
            user = session.Instance.AllUsers.FirstOrDefault(u => u.UserName == request.UserName);
        }
        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The user does not appear to be in a session!"));
        }

        session.Instance.RunSynchronously(() =>
        {
            user.Kick();
        });

        return Task.FromResult(new KickUserResponse());
    }

    public override Task<BanUserResponse> BanUser(BanUserRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        FrooxEngine.User? user = null;
        if (request.HasUserId)
        {
            user = session.Instance.AllUsers.FirstOrDefault(u => u.UserID == request.UserId);
        }
        else if (request.HasUserName)
        {
            user = session.Instance.AllUsers.FirstOrDefault(u => u.UserName == request.UserName);
        }
        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The user does not appear to be in a session!"));
        }

        session.Instance.RunSynchronously(() =>
        {
            user.Ban();
        });

        return Task.FromResult(new BanUserResponse());
    }
}
