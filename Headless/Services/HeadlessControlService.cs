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
    private readonly IFrooxEngineRunnerService _runnerService;

    private bool _isShutdownRequested = false;

    public HeadlessControlService
    (
        ILogger<HeadlessControlService> logger,
        IHostApplicationLifetime applicationLifetime,
        Engine engine,
        WorldService worldService,
        IFrooxEngineRunnerService runnerService
    )
    {
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _engine = engine;
        _worldService = worldService;
        _runnerService = runnerService;

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
            IdleRestartInterval = reqParam.IdleRestartIntervalSeconds == 0 ? -1 : reqParam.IdleRestartIntervalSeconds,
            SaveOnExit = reqParam.SaveOnExit,
            AutoSaveInterval = reqParam.AutoSaveIntervalSeconds == 0 ? -1 : reqParam.AutoSaveIntervalSeconds,
            AutoSleep = reqParam.AutoSleep,
            AutoRecover = reqParam.AutoRecover,
            ForcePort = reqParam.ForcePort == 0 ? null : (ushort)reqParam.ForcePort,
            ParentSessionIds = reqParam.ParentSessionIds.ToList(),
            ForcedRestartInterval = reqParam.ForcedRestartIntervalSeconds == 0 ? -1 : reqParam.ForcedRestartIntervalSeconds,
            InviteRequestHandlerUsernames = reqParam.InviteRequestHandlerUsernames.ToList(),
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

        session.WorldInstance.RunSynchronously(() =>
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
            user.Ban();
        });

        return Task.FromResult(new BanUserResponse());
    }

    public override Task<GetAccountInfoResponse> GetAccountInfo(GetAccountInfoRequest request, ServerCallContext context)
    {
        var cloud = _engine.Cloud;
        if (cloud.CurrentUser is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Headless is not login"));
        }

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

    public override Task<GetFriendRequestsResponse> GetFriendRequests(GetFriendRequestsRequest request, ServerCallContext context)
    {
        var contacts = new List<Contact>();
        _engine.Cloud.Contacts.GetContacts(contacts);
        var userInfos = contacts.FindAll(c => c.ContactStatus == ContactStatus.Requested).Select(c => new Rpc.UserInfo
        {
            Id = c.ContactUserId,
            Name = c.ContactUsername,
            IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? ""
        });
        return Task.FromResult(new GetFriendRequestsResponse
        {
            Users = { userInfos }
        });
    }

    public override Task<AcceptFriendRequestsResponse> AcceptFriendRequests(AcceptFriendRequestsRequest request, ServerCallContext context)
    {
        foreach (var userId in request.UserIds)
        {
            var contact = _engine.Cloud.Contacts.GetContact(userId);
            if (contact.ContactStatus == ContactStatus.Requested)
            {
                _engine.Cloud.Contacts.AddContact(contact);
            }
        }
        return Task.FromResult(new AcceptFriendRequestsResponse { });
    }

    public override Task<ListContactsResponse> ListContacts(ListContactsRequest request, ServerCallContext context)
    {
        var contacts = new List<Contact>();
        _engine.Cloud.Contacts.GetContacts(contacts);
        var userInfos = contacts.Take(request.Limit).Select(c => new Rpc.UserInfo
        {
            Id = c.ContactUserId,
            Name = c.ContactUsername,
            IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? ""
        });
        return Task.FromResult(new ListContactsResponse
        {
            Users = { userInfos }
        });
    }

    public override async Task<GetContactMessagesResponse> GetContactMessages(GetContactMessagesRequest request, ServerCallContext context)
    {
        var result = await _engine.Cloud.Messages.GetMessageHistory(request.UserId, request.Limit);
        if (result.IsError)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed fetch from resonite cloud"));
        }
        var protoMessages = result.Entity.Select(m => new Rpc.ContactChatMessage
        {
            Id = m.Id,
            Type = m.MessageType switch
            {
                SkyFrost.Base.MessageType.Object => ContactChatMessageType.Object,
                SkyFrost.Base.MessageType.Sound => ContactChatMessageType.Sound,
                SkyFrost.Base.MessageType.Text => ContactChatMessageType.Text,
                SkyFrost.Base.MessageType.SessionInvite => ContactChatMessageType.SessionInvite,
                _ => ContactChatMessageType.Unspecified
            },
            Content = m.Content,
            SendTime = Timestamp.FromDateTime(m.SendTime),
            ReadTime = m.ReadTime is not null ? Timestamp.FromDateTime((DateTime)m.ReadTime) : null,
        }).Reverse();
        return new GetContactMessagesResponse
        {
            Messages = { protoMessages }
        };
    }

    public override async Task<SendContactMessageResponse> SendContactMessage(SendContactMessageRequest request, ServerCallContext context)
    {
        await _engine.Cloud.Messages.GetUserMessages(request.UserId).SendTextMessage(request.Message);

        return new SendContactMessageResponse();
    }

    public override async Task<GetHostSettingsResponse> GetHostSettings(GetHostSettingsRequest request, ServerCallContext context)
    {
        var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
        var allowedList = securitySettings.Entries.Select(entry =>
        {
            var types = new List<AllowedAccessEntry.Types.AccessType>();
            if (entry.Value.AllowHTTP_Requests)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Http);
            }
            if (entry.Value.AllowWebsockets)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.Websocket);
            }
            if (entry.Value.AllowOSC_Receiving)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscReceiving);
            }
            if (entry.Value.AllowOSC_Sending)
            {
                types.Add(AllowedAccessEntry.Types.AccessType.OscSending);
            }
            return new AllowedAccessEntry
            {
                Host = entry.Key,
                Ports = { entry.Value.AllowedPorts },
                AccessTypes = { types },
            };
        });
        var response = new GetHostSettingsResponse
        {
            TickRate = _runnerService.TickRate,
            MaxConcurrentAssetTransfers = SessionAssetTransferer.OverrideMaxConcurrentTransfers ?? 4,
            AllowedUrlHosts = { allowedList },
            AutoSpawnItems = { _worldService.AutoSpawnItems.Select(uri => uri.ToString()) },
        };
        if (_engine.Cloud.UniverseID is not null)
        {
            response.UniverseId = _engine.Cloud.UniverseID;
        }
        if (_engine.UsernameOverride is not null)
        {
            response.UsernameOverride = _engine.UsernameOverride;
        }

        return response;
    }

    public override Task<UpdateHostSettingsResponse> UpdateHostSettings(UpdateHostSettingsRequest request, ServerCallContext context)
    {
        if (request.HasTickRate && request.TickRate > 0)
        {
            _runnerService.TickRate = request.TickRate;
        }
        if (request.HasMaxConcurrentAssetTransfers && request.MaxConcurrentAssetTransfers > 0)
        {
            SessionAssetTransferer.OverrideMaxConcurrentTransfers = request.MaxConcurrentAssetTransfers;
        }
        if (request.HasUsernameOverride && request.UsernameOverride.Length > 0)
        {
            _engine.UsernameOverride = request.UsernameOverride;
        }
        if (request.UpdateAutoSpawnItems)
        {
            _worldService.AutoSpawnItems = request.AutoSpawnItems.Select(uri =>
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
                {
                    return result;
                }
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid item URL: {uri}"));
            }).ToList();
        }

        return Task.FromResult(new UpdateHostSettingsResponse());
    }

    public override Task<AllowHostAccessResponse> AllowHostAccess(AllowHostAccessRequest request, ServerCallContext context)
    {
        Userspace.UserspaceWorld.RunSynchronously(async () =>
        {
            var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
            switch (request.AccessType)
            {
                case AllowedAccessEntry.Types.AccessType.Http:
                    securitySettings.AllowHTTP_Requests(request.Host, request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.Websocket:
                    securitySettings.AllowWebsocket(request.Host, request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscReceiving:
                    securitySettings.AllowOSC_Receiving(request.Port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscSending:
                    securitySettings.AllowOSC_Sending(request.Host, request.Port);
                    break;
            }
        });

        return Task.FromResult(new AllowHostAccessResponse());
    }

    public override Task<DenyHostAccessResponse> DenyHostAccess(DenyHostAccessRequest request, ServerCallContext context)
    {
        Userspace.UserspaceWorld.RunSynchronously(async () =>
        {
            var securitySettings = await Settings.GetActiveSettingAsync<HostAccessSettings>();
            int? port = null;
            if (request.HasPort && request.Port > 0)
            {
                port = request.Port;
            }
            switch (request.AccessType)
            {
                case AllowedAccessEntry.Types.AccessType.Http:
                    securitySettings.BlockHTTP_Requests(request.Host, port);
                    break;
                case AllowedAccessEntry.Types.AccessType.Websocket:
                    securitySettings.BlockWebsocket(request.Host, port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscReceiving:
                    securitySettings.BlockOSC_Receiving(port);
                    break;
                case AllowedAccessEntry.Types.AccessType.OscSending:
                    securitySettings.BlockOSC_Sending(request.Host, port);
                    break;
            }
        });

        return Task.FromResult(new DenyHostAccessResponse());
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
            StartedAt = Timestamp.FromDateTime(info.SessionBeginTime),
            AwayKickMinutes = info.AwayKickEnabled ? info.AwayKickMinutes : -1,
            IdleRestartIntervalSeconds = (int)session.IdleRestartInterval.TotalSeconds,
            SaveOnExit = session.WorldInstance.SaveOnExit,
            AutoSaveIntervalSeconds = (int)session.AutosaveInterval.TotalSeconds,
            HideFromPublicListing = info.HideFromListing,
            LastSavedAt = Timestamp.FromDateTimeOffset(session.LastSaveTime),
            CanSave = Userspace.CanSave(session.WorldInstance),
        };
        if (session.WorldInstance.RecordURL != null)
        {
            result.WorldUrl = session.WorldInstance.RecordURL.ToString();
        }
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
            ForcePort = parameters.ForcePort ?? 0,
            ParentSessionIds = { parameters.ParentSessionIds ?? [] },
            AutoRecover = parameters.AutoRecover,
            ForcedRestartIntervalSeconds = (int)parameters.ForcedRestartInterval,
            InviteRequestHandlerUsernames = { parameters.InviteRequestHandlerUsernames ?? [] }
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