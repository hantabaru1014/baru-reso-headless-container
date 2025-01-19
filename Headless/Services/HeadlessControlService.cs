using FrooxEngine;
using Grpc.Core;
using SkyFrost.Base;
using Headless.Rpc;
using Google.Protobuf.WellKnownTypes;
using Headless.Libs;

namespace Headless.Services;

public class HeadlessControlService : Rpc.HeadlessControlService.HeadlessControlServiceBase
{
    private readonly ILogger<HeadlessControlService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly Engine _engine;
    private readonly WorldService _worldService;

    private bool _isShutdownRequested = false;

    public HeadlessControlService
    (
        ILogger<HeadlessControlService> logger,
        IHostApplicationLifetime applicationLifetime,
        Engine engine,
        WorldService worldService
    )
    {
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _engine = engine;
        _worldService = worldService;

        CloudUtils.Setup(_engine.Cloud.Assets);
    }

    public override Task<GetAboutResponse> GetAbout(GetAboutRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetAboutResponse
        {
            AppVersion = "<TODO>",
            ResoniteVersion = _engine.VersionString,
        });
    }

    public override Task<GetStatusResponse> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetStatusResponse
        {
            Fps = _engine.SystemInfo.FPS,
            TotalEngineUpdateTime = _engine.TotalEngineUpdateTime,
            SyncingRecordsCount = _engine.RecordManager.SyncingRecordsCount,
        });
    }

    public override Task<ShutdownResponse> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        if (!_isShutdownRequested)
        {
            _isShutdownRequested = true;

            // OnShutdownRequest をフックにしてStandaloneFrooxEngineServiceのctをキャンセルするが、
            // そのままRequestShutdownをしてしまうとEngineの更新が止まって保存したワールドのアップロードが開始しないので一回だけCancelShutdownしておく
            _engine.CancelShutdown();
            _engine.RequestShutdown();
        }
        return Task.FromResult(new ShutdownResponse());
    }

    public override Task<ListSessionsResponse> ListSessions(ListSessionsRequest request, ServerCallContext context)
    {
        var reply = new ListSessionsResponse();
        foreach (var session in _worldService.ListAll())
        {
            reply.Sessions.Add(ToRpcSession(session));
        }
        return Task.FromResult(reply);
    }

    public override Task<GetSessionResponse> GetSession(GetSessionRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        return Task.FromResult(new GetSessionResponse
        {
            Session = ToRpcSession(session)
        });
    }

    public override async Task<StartWorldResponse> StartWorld(StartWorldRequest request, ServerCallContext context)
    {
        if (_isShutdownRequested) throw new RpcException(new Status(StatusCode.Unavailable, "Already shutting down"));

        var reqParam = request.Parameters;
        if (!reqParam.HasLoadWorldUrl && !reqParam.HasLoadWorldPresetName)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require load_world_url or load_world_preset_name!"));
        }
        var parameters = new SkyFrost.Base.WorldStartupParameters
        {
            SessionName = reqParam.HasName ? reqParam.Name : null,
            CustomSessionId = reqParam.HasCustomSessionId ? reqParam.CustomSessionId : null,
            Description = reqParam.HasDescription ? reqParam.Description : null,
            Tags = reqParam.Tags.ToList(),
            AccessLevel = ToSessionAccessLevel(reqParam.AccessLevel),
            LoadWorldURL = reqParam.HasLoadWorldUrl ? reqParam.LoadWorldUrl : null,
            LoadWorldPresetName = reqParam.HasLoadWorldPresetName ? reqParam.LoadWorldPresetName : null,
            AutoInviteUsernames = reqParam.AutoInviteUsernames.ToList(),
            DefaultUserRoles = reqParam.DefaultUserRoles.ToDictionary(p => p.UserName, p => p.Role),
            HideFromPublicListing = reqParam.HideFromPublicListing,
            AwayKickMinutes = reqParam.AwayKickMinutes == 0 ? -1 : reqParam.AwayKickMinutes,
            IdleRestartInterval = reqParam.IdleRestartIntervalSeconds,
            SaveOnExit = reqParam.SaveOnExit,
            AutoSaveInterval = reqParam.AutoSaveIntervalSeconds,
            AutoSleep = reqParam.AutoSleep,
            AutoRecover = true,
        };
        if (reqParam.HasMaxUsers)
        {
            parameters.MaxUsers = reqParam.MaxUsers;
        }
        var session = await _worldService.StartWorldAsync(parameters);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed open world!"));
        }
        return new StartWorldResponse
        {
            OpenedSession = ToRpcSession(session)
        };
    }

    public override async Task<StopSessionResponse> StopSession(StopSessionRequest request, ServerCallContext context)
    {
        await _worldService.StopWorldAsync(request.SessionId);
        return new StopSessionResponse();
    }

    public override async Task<SaveSessionWorldResponse> SaveSessionWorld(SaveSessionWorldRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        await _worldService.SaveWorldAsync(session);
        return new SaveSessionWorldResponse();
    }

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

    public override async Task<UpdateUserRoleResponse> UpdateUserRole(UpdateUserRoleRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        var permissionSet = session.WorldInstance.Permissions.Roles.FirstOrDefault(r => r.RoleName.Value.Equals(request.Role, StringComparison.InvariantCultureIgnoreCase));
        if (permissionSet is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid role name"));
        }
        FrooxEngine.User? user = null;
        if (request.HasUserId)
        {
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserID == request.UserId);
        }
        else if (request.HasUserName)
        {
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserName == request.UserName);
        }
        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The user does not appear to be in a session!"));
        }
        session.WorldInstance.RunSynchronously(() =>
        {
            user.Role = permissionSet;
            session.WorldInstance.Permissions.AssignDefaultRole(user, permissionSet);
        });

        await Task.CompletedTask;
        return new UpdateUserRoleResponse
        {
            Role = user.Role.RoleName.Value
        };
    }

    public override async Task<UpdateSessionParametersResponse> UpdateSessionParameters(UpdateSessionParametersRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        if (request.HasName)
        {
            session.WorldInstance.Name = request.Name;
        }
        if (request.HasDescription)
        {
            session.WorldInstance.Description = request.Description;
        }
        if (request.HasMaxUsers)
        {
            session.WorldInstance.MaxUsers = request.MaxUsers;
        }
        if (request.HasAccessLevel)
        {
            session.WorldInstance.AccessLevel = ToSessionAccessLevel(request.AccessLevel);
        }
        if (request.HasAwayKickMinutes)
        {
            session.WorldInstance.AwayKickMinutes = request.AwayKickMinutes;
        }
        if (request.HasIdleRestartIntervalSeconds)
        {
            session.IdleRestartInterval = TimeSpan.FromSeconds(request.IdleRestartIntervalSeconds);
        }
        if (request.HasSaveOnExit)
        {
            session.WorldInstance.SaveOnExit = request.SaveOnExit;
        }
        if (request.HasAutoSaveIntervalSeconds)
        {
            session.AutosaveInterval = TimeSpan.FromSeconds(request.AutoSaveIntervalSeconds);
        }
        if (request.HasHideFromPublicListing)
        {
            session.WorldInstance.HideFromListing = request.HideFromPublicListing;
        }
        if (request.UpdateTags)
        {
            session.WorldInstance.Tags = request.Tags;
        }
        await Task.CompletedTask;
        return new UpdateSessionParametersResponse();
    }

    public override async Task<ListUsersInSessionResponse> ListUsersInSession(ListUsersInSessionRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        var users = session.WorldInstance.AllUsers.Select(user => new Rpc.UserInSession
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
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserID == request.UserId);
        }
        else if (request.HasUserName)
        {
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserName == request.UserName);
        }
        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The user does not appear to be in a session!"));
        }

        user.Kick();

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
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserID == request.UserId);
        }
        else if (request.HasUserName)
        {
            user = session.WorldInstance.AllUsers.FirstOrDefault(u => u.UserName == request.UserName);
        }
        if (user is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"The user does not appear to be in a session!"));
        }

        user.Ban();

        return Task.FromResult(new BanUserResponse());
    }

    public override Task<GetAccountInfoResponse> GetAccountInfo(GetAccountInfoRequest request, ServerCallContext context)
    {
        var cloud = _engine.Cloud;
        var storage = cloud.Storage.CurrentStorage;

        return Task.FromResult(new GetAccountInfoResponse
        {
            UserId = cloud.CurrentUserID,
            DisplayName = cloud.CurrentUsername,
            StorageQuotaBytes = storage.QuotaBytes,
            StorageUsedBytes = storage.UsedBytes,
        });
    }

    public override async Task<FetchWorldInfoResponse> FetchWorldInfo(FetchWorldInfoRequest request, ServerCallContext context)
    {
        var cloudResult = await _engine.RecordManager.FetchRecord(new Uri(request.Url));
        if (!cloudResult.IsOK)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed to fetch record"));
        }
        var record = cloudResult.Entity;
        var canModify = _engine.RecordManager.CanModify(record);

        return new FetchWorldInfoResponse
        {
            Name = record.Name ?? "Unnamed",
            Description = record.Description ?? "",
            ThumbnailUrl = CloudUtils.ResolveURL(record.ThumbnailURI) ?? "",
            DefaultMaxUsers = -1, // TODO
            OwnerId = record.OwnerId ?? "",
            IsPublic = record.IsPublic,
            CanModify = canModify,
            IsReadonly = record.IsReadOnly,
            Tags = { record.Tags ?? [] },
        };
    }

    public override async Task<SearchUserInfoResponse> SearchUserInfo(SearchUserInfoRequest request, ServerCallContext context)
    {
        if (request.HasUserId && request.UserId.Length < 2)
        {
            return new SearchUserInfoResponse();
        }
        if (request.HasUserName && request.UserName.Length == 0)
        {
            return new SearchUserInfoResponse();
        }

        var contactResult = new List<Contact>();
        _engine.Cloud.Contacts.ForeachContact(c =>
        {
            if (request.HasUserId)
            {
                if (request.PartialMatch)
                {
                    if (c.ContactUserId.Contains(request.UserId))
                    {
                        contactResult.Add(c);
                    }
                }
                else
                {
                    if (c.ContactUserId.Equals(request.UserId, StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
            }
            else
            {
                if (request.PartialMatch)
                {
                    if (c.ContactUsername.Contains(request.UserName.Trim().ToLower(), StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
                else
                {
                    if (c.ContactUsername.Equals(request.UserName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        contactResult.Add(c);
                    }
                }
            }
        });
        var result = contactResult.Select(c => new Rpc.UserInfo { Id = c.ContactUserId, Name = c.ContactUsername, IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? "" }).ToList();
        if (!request.OnlyInContacts && request.HasUserName)
        {
            var cloudResult = await _engine.Cloud.Users.GetUsers(request.UserName.Trim().ToLower());
            if (cloudResult.IsOK)
            {
                result.Concat(cloudResult.Entity.Select(r => new Rpc.UserInfo { Id = r.Id, Name = r.Username, IconUrl = "" }));
            }
        }
        return new SearchUserInfoResponse
        {
            Users = { result }
        };
    }

    public static Rpc.AccessLevel ToRpcAccessLevel(SessionAccessLevel level)
    {
        return level switch
        {
            SessionAccessLevel.Private => AccessLevel.Private,
            SessionAccessLevel.LAN => AccessLevel.Lan,
            SessionAccessLevel.Contacts => AccessLevel.Contacts,
            SessionAccessLevel.ContactsPlus => AccessLevel.ContactsPlus,
            SessionAccessLevel.RegisteredUsers => AccessLevel.RegisteredUsers,
            SessionAccessLevel.Anyone => AccessLevel.Anyone,
            _ => AccessLevel.Unspecified
        };
    }

    public static SessionAccessLevel ToSessionAccessLevel(AccessLevel level)
    {
        return level switch
        {
            AccessLevel.Private => SessionAccessLevel.Private,
            AccessLevel.Lan => SessionAccessLevel.LAN,
            AccessLevel.Contacts => SessionAccessLevel.Contacts,
            AccessLevel.ContactsPlus => SessionAccessLevel.ContactsPlus,
            AccessLevel.RegisteredUsers => SessionAccessLevel.RegisteredUsers,
            AccessLevel.Anyone => SessionAccessLevel.Anyone,
            _ => SessionAccessLevel.Private
        };
    }

    public static Rpc.Session ToRpcSession(RunningSession session)
    {
        var info = session.WorldInstance.GenerateSessionInfo();
        var result = new Rpc.Session
        {
            Id = info.SessionId,
            Name = info.Name ?? "<Empty Name>",
            Description = info.Description ?? "",
            Tags = { info.Tags ?? [] },
            AccessLevel = ToRpcAccessLevel(info.AccessLevel),
            StartupParameters = ToRpcStartupParams(session.StartInfo),
            UsersCount = info.JoinedUsers,
            MaxUsers = info.MaximumUsers,
            SessionUrl = CloudUtils.MakeSessionGoURL(info.SessionId),
            TimeRunningMs = (int)Math.Round(session.TimeRunning.TotalMilliseconds),
            AwayKickMinutes = info.AwayKickMinutes,
            IdleRestartIntervalSeconds = (int)session.IdleRestartInterval.TotalSeconds,
            SaveOnExit = session.WorldInstance.SaveOnExit,
            AutoSaveIntervalSeconds = (int)session.AutosaveInterval.TotalSeconds,
            HideFromPublicListing = info.HideFromListing,
            LastSavedAt = Timestamp.FromDateTimeOffset(session.LastSaveTime),
            CanSave = Userspace.CanSave(session.WorldInstance),
        };
        if (info.ThumbnailUrl is not null)
        {
            result.ThumbnailUrl = CloudUtils.ResolveURL(info.ThumbnailUrl);
        }
        return result;
    }

    public static Rpc.WorldStartupParameters ToRpcStartupParams(SkyFrost.Base.WorldStartupParameters parameters)
    {
        var result = new Rpc.WorldStartupParameters
        {
            MaxUsers = parameters.MaxUsers,
            AccessLevel = ToRpcAccessLevel(parameters.AccessLevel),
            AutoInviteUsernames = { parameters.AutoInviteUsernames ?? [] },
            Tags = { parameters.Tags ?? [] },
            HideFromPublicListing = parameters.HideFromPublicListing ?? false,
            DefaultUserRoles = { parameters.DefaultUserRoles?.Select(p => new Rpc.DefaultUserRole { UserName = p.Key, Role = p.Value }) },
            AwayKickMinutes = (float)parameters.AwayKickMinutes,
            IdleRestartIntervalSeconds = (int)parameters.IdleRestartInterval,
            SaveOnExit = parameters.SaveOnExit,
            AutoSaveIntervalSeconds = (int)parameters.AutoSaveInterval,
            AutoSleep = parameters.AutoSleep,
        };
        if (parameters.SessionName is not null)
        {
            result.Name = parameters.SessionName;
        }
        if (parameters.CustomSessionId is not null)
        {
            result.CustomSessionId = parameters.CustomSessionId;
        }
        if (parameters.Description is not null)
        {
            result.Description = parameters.Description;
        }
        if (parameters.LoadWorldURL is not null)
        {
            result.LoadWorldUrl = parameters.LoadWorldURL;
        }
        else
        {
            result.LoadWorldPresetName = parameters.LoadWorldPresetName;
        }
        return result;
    }
}