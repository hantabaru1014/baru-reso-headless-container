using FrooxEngine;
using Grpc.Core;
using SkyFrost.Base;
using Headless.Rpc;

namespace Headless.Services;

public class HeadlessControlService : HeadlessControl.HeadlessControlBase
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

    public override Task<ShutdownReply> Shutdown(ShutdownRequest request, ServerCallContext context)
    {
        _applicationLifetime.StopApplication();
        return Task.FromResult(new ShutdownReply());
    }

    public override Task<ListSessionsReply> ListSessions(ListSessionsRequest request, ServerCallContext context)
    {
        var reply = new ListSessionsReply();
        foreach (var session in _worldService.ListAll())
        {
            reply.Sessions.Add(ToRpcSession(session));
        }
        return Task.FromResult(reply);
    }

    public override async Task<StartWorldReply> StartWorld(StartWorldRequest request, ServerCallContext context)
    {
        var reqParam = request.Parameters;
        var worldUrl = string.IsNullOrWhiteSpace(reqParam.LoadWorldUrl) ? null : reqParam.LoadWorldUrl;
        var presetName = string.IsNullOrWhiteSpace(reqParam.LoadWorldPresetName) ? null : reqParam.LoadWorldPresetName;
        if (worldUrl is null && presetName is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require load_world_url or load_world_preset_name!"));
        }
        var parameters = new SkyFrost.Base.WorldStartupParameters
        {
            SessionName = reqParam.SessionName,
            CustomSessionId = reqParam.CustomSessionId,
            Description = reqParam.Description,
            MaxUsers = reqParam.MaxUsers,
            AccessLevel = ToSessionAccessLevel(reqParam.AccessLevel),
            LoadWorldURL = worldUrl,
            LoadWorldPresetName = presetName,
            AutoInviteUsernames = reqParam.AutoInviteUsernames.ToList()
        };
        var session = await _worldService.StartWorldAsync(parameters);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed open world!"));
        }
        return new StartWorldReply
        {
            OpenedSession = ToRpcSession(session)
        };
    }

    public override async Task<StopSessionReply> StopSession(StopSessionRequest request, ServerCallContext context)
    {
        await _worldService.StopWorldAsync(request.SessionId);
        return new StopSessionReply();
    }

    public override async Task<InviteUserReply> InviteUser(InviteUserRequest request, ServerCallContext context)
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
        return new InviteUserReply();
    }

    public override async Task<UpdateUserRoleReply> UpdateUserRole(UpdateUserRoleRequest request, ServerCallContext context)
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
        session.WorldInstance.RunSynchronously(() => {
            user.Role = permissionSet;
            session.WorldInstance.Permissions.AssignDefaultRole(user, permissionSet);
        });

        await Task.CompletedTask;
        return new UpdateUserRoleReply{
            Role = user.Role.RoleName.Value
        };
    }

    public override async Task<UpdateSessionParametersReply> UpdateSessionParameters(UpdateSessionParametersRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        foreach (var param in request.Parameters)
        {
            if (param.HasSessionName)
            {
                session.WorldInstance.Name = param.SessionName;
                continue;
            }
            if (param.HasDescription)
            {
                session.WorldInstance.Description = param.Description;
                continue;
            }
            if (param.HasMaxUsers)
            {
                session.WorldInstance.MaxUsers = param.MaxUsers;
                continue;
            }
            if (param.HasAccessLevel)
            {
                session.WorldInstance.AccessLevel = ToSessionAccessLevel(param.AccessLevel);
                continue;
            }
        }
        await Task.CompletedTask;
        return new UpdateSessionParametersReply();
    }

    public static Rpc.AccessLevel ToRpcAccessLevel(SessionAccessLevel level)
    {
        return level switch {
            SessionAccessLevel.Private => AccessLevel.Private,
            SessionAccessLevel.LAN => AccessLevel.Lan,
            SessionAccessLevel.Contacts => AccessLevel.Contacts,
            SessionAccessLevel.ContactsPlus => AccessLevel.ContactsPlus,
            SessionAccessLevel.RegisteredUsers => AccessLevel.RegisteredUsers,
            SessionAccessLevel.Anyone => AccessLevel.Anyone,
            _ => AccessLevel.Unknown
        };
    }

    public static SessionAccessLevel ToSessionAccessLevel(AccessLevel level)
    {
        return level switch {
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
        var users = session.WorldInstance.AllUsers.Select(user => new Rpc.UserInSession{
            Id = user.UserID,
            Name = user.UserName,
            Role = user.Role.RoleName.Value,
            IsPresent = user.IsPresent
        });
        var result = new Rpc.Session{
            Id = info.SessionId,
            Name = info.Name ?? "<Empty Name>",
            Description = info.Description ?? "",
            AccessLevel = ToRpcAccessLevel(info.AccessLevel),
            Users = {users},
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
        var result = new Rpc.WorldStartupParameters{
            MaxUsers = parameters.MaxUsers,
            AccessLevel = ToRpcAccessLevel(parameters.AccessLevel),
            AutoInviteUsernames = {parameters.AutoInviteUsernames ?? []}
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