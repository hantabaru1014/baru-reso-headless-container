using FrooxEngine;
using Grpc.Core;
using SkyFrost.Base;
using Headless.Rpc;

namespace Headless.Services;

public class HeadlessControlService : Rpc.HeadlessControlService.HeadlessControlServiceBase
{
    private readonly ILogger<HeadlessControlService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly Engine _engine;
    private readonly WorldService _worldService;

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
        _applicationLifetime.StopApplication();
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

    public override async Task<StartWorldResponse> StartWorld(StartWorldRequest request, ServerCallContext context)
    {
        var reqParam = request.Parameters;
        if (!reqParam.HasLoadWorldUrl && !reqParam.HasLoadWorldPresetName)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require load_world_url or load_world_preset_name!"));
        }
        var parameters = new SkyFrost.Base.WorldStartupParameters
        {
            SessionName = reqParam.HasSessionName ? reqParam.SessionName : null,
            CustomSessionId = reqParam.HasCustomSessionId ? reqParam.CustomSessionId : null,
            Description = reqParam.HasDescription ? reqParam.Description : null,
            AccessLevel = ToSessionAccessLevel(reqParam.AccessLevel),
            LoadWorldURL = reqParam.HasLoadWorldUrl ? reqParam.LoadWorldUrl : null,
            LoadWorldPresetName = reqParam.HasLoadWorldPresetName ? reqParam.LoadWorldPresetName : null,
            AutoInviteUsernames = reqParam.AutoInviteUsernames.ToList()
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
        if (request.HasSessionName)
        {
            session.WorldInstance.Name = request.SessionName;
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
            AccessLevel = ToRpcAccessLevel(info.AccessLevel),
            StartupParameters = ToRpcStartupParams(session.StartInfo)
        };
        if (info.ThumbnailUrl is not null)
        {
            result.ThumbnailUrl = info.ThumbnailUrl;
        }
        return result;
    }

    public static Rpc.WorldStartupParameters ToRpcStartupParams(SkyFrost.Base.WorldStartupParameters parameters)
    {
        var result = new Rpc.WorldStartupParameters
        {
            MaxUsers = parameters.MaxUsers,
            AccessLevel = ToRpcAccessLevel(parameters.AccessLevel),
            AutoInviteUsernames = { parameters.AutoInviteUsernames ?? [] }
        };
        if (parameters.SessionName is not null)
        {
            result.SessionName = parameters.SessionName;
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