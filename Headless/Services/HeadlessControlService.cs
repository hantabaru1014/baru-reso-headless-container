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
            AccessLevel = reqParam.AccessLevel switch {
                AccessLevel.Private => SessionAccessLevel.Private,
                AccessLevel.Lan => SessionAccessLevel.LAN,
                AccessLevel.Contacts => SessionAccessLevel.Contacts,
                AccessLevel.ContactsPlus => SessionAccessLevel.ContactsPlus,
                AccessLevel.RegisteredUsers => SessionAccessLevel.RegisteredUsers,
                AccessLevel.Anyone => SessionAccessLevel.Anyone,
                _ => SessionAccessLevel.Private
            },
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

    public static Rpc.Session ToRpcSession(RunningSession session)
    {
        var users = session.WorldInstance.AllUsers.Select(user => new Rpc.User{
            Id = user.UserID,
            Name = user.UserName
        });
        return new Rpc.Session{
            Id = session.WorldInstance.SessionId,
            Name = session.WorldInstance.Name ?? "<Empty Name>",
            Description = session.WorldInstance.Description ?? "",
            AccessLevel = ToRpcAccessLevel(session.WorldInstance.AccessLevel),
            Users = {users},
            ThumbnailUrl = "",
            StartupParameters = ToRpcStartupParams(session.StartInfo)
        };
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
        if (parameters.LoadWorldPresetName is not null)
        {
            result.LoadWorldPresetName = parameters.LoadWorldPresetName;
        }
        else
        {
            result.LoadWorldUrl = parameters.LoadWorldURL;
        }
        return result;
    }
}