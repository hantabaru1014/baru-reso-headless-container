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

    public override async Task<ListBansResponse> ListBans(ListBansRequest request, ServerCallContext context)
    {
        // Ban はホスト単位の UserRestrictionsSettings に保持されているためセッション ID は不要。
        // Entries / MachineIDs は UserspaceWorld 側 (engine スレッド) で mutate される SyncList / SyncFieldList
        // なので、gRPC スレッドから直接列挙すると collection-mutated 例外を踏みうる。
        // ComputeWithActiveSettingAsync は setting.RunSynchronouslyAsync で compute を engine スレッド上で
        // 走らせるため、その中で列挙と DTO 化を完結させる。
        var bans = await Settings.ComputeWithActiveSettingAsync<List<Rpc.BanEntry>, UserRestrictionsSettings>(r =>
            r.Entries
                .Where(entry => entry.IsFullyBanned.Value)
                .Select(entry => new Rpc.BanEntry
                {
                    UserId = entry.UserId.Value ?? string.Empty,
                    UserName = entry.Username.Value ?? string.Empty,
                    MachineIds = { entry.MachineIDs.ToList() },
                })
                .ToList());
        return new ListBansResponse
        {
            Bans = { bans }
        };
    }

    public override async Task<UnbanUserResponse> UnbanUser(UnbanUserRequest request, ServerCallContext context)
    {
        // HasUserId / HasUserName は空文字列を明示 set しても true になるため、
        // 中身が空のケースは InvalidArgument に落として NotFound と区別する。
        string? filterUserId = null;
        string? filterUserName = null;
        if (request.HasUserId && !string.IsNullOrEmpty(request.UserId))
        {
            filterUserId = request.UserId;
        }
        else if (request.HasUserName && !string.IsNullOrEmpty(request.UserName))
        {
            filterUserName = request.UserName;
        }
        else
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require valid user_id or user_name"));
        }

        // UserRestrictionsSettings.FindMatchingEntry は _byUserId / _byMachineId / _byExtraId しか索引しない。
        // Username のみで作った fingerprint を渡すと GetEntryForUpdate が新規 entry を Entries に追加してしまい、
        // 元の ban が解除されずに空 entry (IsFullyBanned=false) だけが増える。
        // 該当 entry を線形検索して直接 IsFullyBanned=false に落とすことで name-only ban も解除できる。
        //
        // Settings.UpdateActiveSettingAsync は内部で setting.RunSynchronouslyAsync を呼ぶため、
        // Userspace.UserspaceWorld.RunSynchronously で二重にラップする必要はない。
        // 探索と mutation を同じ tick 内で完結させることで SyncList の thread safety も満たす。
        var found = false;
        await Settings.UpdateActiveSettingAsync<UserRestrictionsSettings>(r =>
        {
            UserRestrictionsSettings.Entry? entry = null;
            if (filterUserId is not null)
            {
                entry = r.Entries.FirstOrDefault(e =>
                    e.IsFullyBanned.Value &&
                    string.Equals(e.UserId.Value, filterUserId, StringComparison.Ordinal));
            }
            else if (filterUserName is not null)
            {
                // Entry の Username は Ban 発行時点のもので、user_id が未セットのこともあるため
                // username で ban entry を検索する
                entry = r.Entries.FirstOrDefault(e =>
                    e.IsFullyBanned.Value &&
                    !string.IsNullOrEmpty(e.Username.Value) &&
                    e.Username.Value.Equals(filterUserName, StringComparison.InvariantCultureIgnoreCase));
            }
            if (entry is null)
            {
                return;
            }
            entry.IsFullyBanned.Value = false;
            found = true;
        });

        if (!found)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "No matching ban entry found"));
        }

        return new UnbanUserResponse();
    }

    public override Task<RespawnUserResponse> RespawnUser(RespawnUserRequest request, ServerCallContext context)
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

        // Vanilla SessionUserController.OnRespawn と同じく、対象ユーザーの Root を
        // DestroyPreservingAssets することでゲーム側の respawn 経路を起動する。
        session.Instance.RunSynchronously(() =>
        {
            user.Root?.Slot?.DestroyPreservingAssets();
        });

        return Task.FromResult(new RespawnUserResponse());
    }
}
