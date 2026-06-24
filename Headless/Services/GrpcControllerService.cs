using FrooxEngine;
using Google.Protobuf;
using Grpc.Core;
using SkyFrost.Base;
using Headless.Rpc;
using Google.Protobuf.WellKnownTypes;
using Headless.Libs;
using Headless.Extensions;
using System.Reflection;

namespace Headless.Services;

public class GrpcControllerService : HeadlessControlService.HeadlessControlServiceBase
{
    private readonly Engine _engine;
    private readonly WorldService _worldService;
    private readonly IFrooxEngineRunnerService _runnerService;
    private readonly ILogger<GrpcControllerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _appVersion;

    private bool _isShutdownRequested = false;

    public GrpcControllerService
    (
        Engine engine,
        WorldService worldService,
        IFrooxEngineRunnerService runnerService,
        ILogger<GrpcControllerService> logger,
        ILoggerFactory loggerFactory
    )
    {
        _engine = engine;
        _worldService = worldService;
        _runnerService = runnerService;
        _logger = logger;
        _loggerFactory = loggerFactory;

        CloudUtils.Setup(_engine.Cloud.Assets);

        _appVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
    }

    public override Task<GetAboutResponse> GetAbout(GetAboutRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetAboutResponse
        {
            AppVersion = _appVersion,
            ResoniteVersion = _engine.VersionString,
        });
    }

    public override Task<GetStatusResponse> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new GetStatusResponse
        {
            Fps = ((SystemInfo)_engine.SystemInfo).FPS, // TODO: もっといい感じにする
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
            reply.Sessions.Add(session.ToProto());
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
            Session = session.ToProto()
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
        var session = await _worldService.StartWorldAsync(reqParam.ToResonite());
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed open world!"));
        }
        return new StartWorldResponse
        {
            OpenedSession = session.ToProto()
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

    public override async Task<SaveAsSessionWorldResponse> SaveAsSessionWorld(SaveAsSessionWorldRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        var saved = await _worldService.SaveWorldAsAsync(session, request.Type == SaveAsSessionWorldRequest.Types.SaveAsType.SaveAs);
        if (saved is null)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed save world"));
        }

        return new SaveAsSessionWorldResponse
        {
            SavedRecordUrl = saved.GetUrl(session.Instance.Engine.Cloud.Platform).ToString()
        };
    }

    public override async Task DownloadSessionWorld(
        DownloadSessionWorldRequest request,
        IServerStreamWriter<DownloadSessionWorldResponse> responseStream,
        ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }
        if (request.Format == WorldBinaryFormat.Unspecified)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Require valid format"));
        }
        if (!session.Instance.IsAllowedToSaveWorld())
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "World is not allowed to be saved"));
        }

        var ct = context.CancellationToken;
        // SDK の書き出し API (LZMAHelper.Compress / PackageCreator.BuildPackage) は
        // 出力ストリームに対し Position/SetLength を要求するためシーク可能なバッキングが必須。
        // テンポラリファイルへ書き出してからチャンク送信する。
        var tmpPath = Path.Combine(Path.GetTempPath(), $"headless-export-{Guid.NewGuid():N}.bin");
        try
        {
            try
            {
                await using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.SequentialScan))
                {
                    await session.ExportWorldBinaryAsync(
                        request.Format,
                        fs,
                        request.HasIncludeVariants && request.IncludeVariants,
                        request.HasBrotliQuality ? request.BrotliQuality : null,
                        ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to export world: {ex.Message}"));
            }

            await using var read = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.SequentialScan | FileOptions.Asynchronous);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var n = await read.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (n == 0) break;
                await responseStream.WriteAsync(new DownloadSessionWorldResponse
                {
                    Chunk = ByteString.CopyFrom(buffer, 0, n),
                }, ct);
            }
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { /* swallow */ }
        }
    }

    public override async Task ResoniteLinkStream(
        IAsyncStreamReader<ResoniteLinkStreamRequest> requestStream,
        IServerStreamWriter<ResoniteLinkStreamResponse> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        if (!await requestStream.MoveNext(ct))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Stream closed before init"));
        }
        var first = requestStream.Current;
        if (first.PayloadCase != ResoniteLinkStreamRequest.PayloadOneofCase.Init)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "First message must be ResoniteLinkInit"));
        }
        var sessionId = first.Init.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "session_id is required"));
        }
        var session = _worldService.GetSession(sessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Session not found"));
        }
        if (!session.Instance.IsAllowedToRunResoniteLink())
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "ResoniteLink is not allowed by the permissions in this world"));
        }

        var bridge = session.GetOrCreateLinkBridge(_loggerFactory.CreateLogger<ResoniteLinkBridge>());
        var client = bridge.OpenClient();
        try
        {
            await responseStream.WriteAsync(new ResoniteLinkStreamResponse { Ready = new ResoniteLinkReady() }, ct);

            var writerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in client.Outgoing.Reader.ReadAllAsync(ct))
                    {
                        await responseStream.WriteAsync(msg, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 通常終了
                }
            }, ct);

            try
            {
                while (await requestStream.MoveNext(ct))
                {
                    var msg = requestStream.Current;
                    switch (msg.PayloadCase)
                    {
                        case ResoniteLinkStreamRequest.PayloadOneofCase.TextFrame:
                            var textBytes = System.Text.Encoding.UTF8.GetBytes(msg.TextFrame);
                            bridge.Dispatch(client, textBytes, System.Net.WebSockets.WebSocketMessageType.Text);
                            break;
                        case ResoniteLinkStreamRequest.PayloadOneofCase.BinaryFrame:
                            bridge.Dispatch(client, msg.BinaryFrame.Memory, System.Net.WebSockets.WebSocketMessageType.Binary);
                            break;
                        case ResoniteLinkStreamRequest.PayloadOneofCase.Init:
                            throw new RpcException(new Status(StatusCode.InvalidArgument, "Init can only be sent once"));
                        default:
                            // 未知の payload は無視
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 通常終了
            }

            // reader 終了に合わせて writer も閉じる
            client.Outgoing.Writer.TryComplete();
            try { await writerTask; } catch (OperationCanceledException) { }
        }
        finally
        {
            bridge.CloseClient(client);
        }
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

    public override async Task<UpdateSessionParametersResponse> UpdateSessionParameters(UpdateSessionParametersRequest request, ServerCallContext context)
    {
        var session = _worldService.GetSession(request.SessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Session not found"));
        }

        // World に触らない validation を先に済ませる。RpcException は engine スレッドの外で投げたい。
        SkyFrost.Base.RecordId? correspondingWorldId = null;
        if (request.OverrideCorrespondingWorldId is not null && !string.IsNullOrEmpty(request.OverrideCorrespondingWorldId.Id))
        {
            var id = request.OverrideCorrespondingWorldId;
            correspondingWorldId = new SkyFrost.Base.RecordId(id.OwnerId, id.Id);
            if (!correspondingWorldId.IsValid)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid CorrespondingWorldId"));
            }
        }
        if (request.HasRoleCloudVariable && !CloudVariableHelper.IsValidPath(request.RoleCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid RoleCloudVariable"));
        }
        if (request.HasAllowUserCloudVariable && !CloudVariableHelper.IsValidPath(request.AllowUserCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid AllowUserCloudVariable"));
        }
        if (request.HasDenyUserCloudVariable && !CloudVariableHelper.IsValidPath(request.DenyUserCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid DenyUserCloudVariable"));
        }
        if (request.HasRequiredUserJoinCloudVariable && !CloudVariableHelper.IsValidPath(request.RequiredUserJoinCloudVariable))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid RequiredUserJoinCloudVariable"));
        }

        // RunningSession ローカルのフィールドは sync 不要
        if (request.HasIdleRestartIntervalSeconds)
        {
            session.IdleRestartInterval = request.IdleRestartIntervalSeconds > 0
                ? TimeSpan.FromSeconds(request.IdleRestartIntervalSeconds)
                : TimeSpan.FromSeconds(-1);
        }
        if (request.HasAutoSaveIntervalSeconds)
        {
            session.AutosaveInterval = request.AutoSaveIntervalSeconds > 0
                ? TimeSpan.FromSeconds(request.AutoSaveIntervalSeconds)
                : TimeSpan.FromSeconds(-1);
        }

        // World への書き込みは engine update スレッドにまとめる。
        // Tags は List 代入、Permissions/Cloud variable 系も内部で collection を触るため、
        // gRPC スレッドから直接代入すると engine 側で iter 中の mutation 例外で落ちうる。
        await session.Instance.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);

            if (request.HasName) session.Instance.Name = request.Name;
            if (request.HasDescription) session.Instance.Description = request.Description;
            if (request.HasMaxUsers) session.Instance.MaxUsers = request.MaxUsers;
            if (request.HasAccessLevel) session.Instance.AccessLevel = request.AccessLevel.ToResonite();
            if (request.HasAwayKickMinutes)
            {
                if (request.AwayKickMinutes > 0)
                {
                    session.Instance.AwayKickEnabled = true;
                    session.Instance.AwayKickMinutes = request.AwayKickMinutes;
                }
                else
                {
                    session.Instance.AwayKickEnabled = false;
                    session.Instance.AwayKickMinutes = -1;
                }
            }
            if (request.HasSaveOnExit) session.Instance.SaveOnExit = request.SaveOnExit;
            if (request.HasHideFromPublicListing) session.Instance.HideFromListing = request.HideFromPublicListing;
            if (request.HasAutoSleep) session.Instance.ForceFullUpdateCycle = !request.AutoSleep;
            if (request.HasUseCustomJoinVerifier) session.Instance.UseCustomJoinVerifier = request.UseCustomJoinVerifier;
            if (request.HasMobileFriendly) session.Instance.MobileFriendly = request.MobileFriendly;
            if (correspondingWorldId is not null) session.Instance.CorrespondingWorldId = correspondingWorldId.ToString();
            if (request.HasRoleCloudVariable) session.Instance.Permissions.DefaultRoleCloudVariable = request.RoleCloudVariable;
            if (request.HasAllowUserCloudVariable) session.Instance.AllowUserCloudVariable = request.AllowUserCloudVariable;
            if (request.HasDenyUserCloudVariable) session.Instance.DenyUserCloudVariable = request.DenyUserCloudVariable;
            if (request.HasRequiredUserJoinCloudVariable) session.Instance.RequiredUserJoinCloudVariable = request.RequiredUserJoinCloudVariable;
            if (request.HasRequiredUserJoinCloudVariableDenyMessage) session.Instance.RequiredUserJoinCloudVariableDenyMessage = request.RequiredUserJoinCloudVariableDenyMessage;
            if (request.UpdateTags) session.Instance.Tags = request.Tags;
        });

        return new UpdateSessionParametersResponse();
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

        // ページネーション処理
        int startIndex = 0;
        if (!string.IsNullOrEmpty(request.Cursor) && int.TryParse(request.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var pagedContacts = contacts.Skip(startIndex).Take(request.Limit + 1).ToList();
        var hasMore = pagedContacts.Count > request.Limit;
        var resultContacts = pagedContacts.Take(request.Limit);

        var userInfos = resultContacts.Select(c => new Rpc.UserInfo
        {
            Id = c.ContactUserId,
            Name = c.ContactUsername,
            IconUrl = CloudUtils.ResolveURL(c.Profile?.IconUrl) ?? ""
        });

        var response = new ListContactsResponse
        {
            Users = { userInfos }
        };
        if (hasMore)
        {
            response.NextCursor = (startIndex + request.Limit).ToString();
        }
        return Task.FromResult(response);
    }

    public override async Task<GetContactMessagesResponse> GetContactMessages(GetContactMessagesRequest request, ServerCallContext context)
    {
        // GetMessages returns messages in newest-first order (index 0 = newest)
        // fromTime fetches messages NEWER than the specified time
        const int maxFetchLimit = 100;
        const int maxRetries = 10;
        var fetchLimit = Math.Min(request.Limit + 1, maxFetchLimit);

        var targetId = !string.IsNullOrEmpty(request.BeforeId) ? request.BeforeId : request.AfterId;

        // Track if there are more older messages in the cloud beyond what we fetched
        bool moreOlderMessagesInCloud = false;

        // First, fetch from the latest (fromTime = null)
        var result = await _engine.Cloud.Messages.GetMessages(null, fetchLimit, request.UserId, false);
        if (result.IsError)
        {
            throw new RpcException(new Status(StatusCode.Internal, "Failed fetch from resonite cloud"));
        }

        var allMessages = result.Entity.ToList();

        // If we got fetchLimit messages, there might be more older messages
        if (allMessages.Count >= fetchLimit)
        {
            moreOlderMessagesInCloud = true;
        }

        // If target specified and not found, search older messages by going back in time
        if (!string.IsNullOrEmpty(targetId) && !allMessages.Any(m => m.Id == targetId) && allMessages.Count > 0)
        {
            var oldestFetchedTime = allMessages.Last().SendTime;

            // Start searching from 1 month back, then keep going back
            var searchFromTime = DateTime.UtcNow.AddMonths(-1);

            for (int retry = 0; retry < maxRetries; retry++)
            {
                result = await _engine.Cloud.Messages.GetMessages(searchFromTime, fetchLimit, request.UserId, false);
                if (result.IsError)
                {
                    throw new RpcException(new Status(StatusCode.Internal, "Failed fetch from resonite cloud"));
                }

                var fetchedMessages = result.Entity;
                if (fetchedMessages.Count == 0)
                {
                    // No messages in this time range, go back further
                    searchFromTime = searchFromTime.AddMonths(-1);
                    continue;
                }

                var newestFetchedTime = fetchedMessages.First().SendTime;
                var oldestInBatchTime = fetchedMessages.Last().SendTime;

                // Add messages that don't overlap with what we already have
                var messagesToAdd = fetchedMessages.Where(m => m.SendTime < oldestFetchedTime).ToList();
                allMessages.AddRange(messagesToAdd);
                oldestFetchedTime = allMessages.Last().SendTime;

                // Update moreOlderMessagesInCloud based on this fetch
                moreOlderMessagesInCloud = fetchedMessages.Count >= fetchLimit;

                // Check if we found the target
                if (allMessages.Any(m => m.Id == targetId))
                {
                    break;
                }

                // Check if ranges have connected (newest in this batch reaches our oldest)
                if (newestFetchedTime >= oldestFetchedTime || oldestInBatchTime >= oldestFetchedTime)
                {
                    // Ranges connected - if target not found, it doesn't exist
                    break;
                }

                // Move search window further back
                searchFromTime = searchFromTime.AddMonths(-1);
            }

            // Sort all messages by SendTime descending (newest first)
            allMessages = allMessages.OrderByDescending(m => m.SendTime).ToList();
        }

        int startIndex = 0;
        int endIndex = allMessages.Count;
        bool hasMoreBefore = false;
        bool hasMoreAfter = false;

        if (!string.IsNullOrEmpty(request.BeforeId))
        {
            // before_id: get messages OLDER than the specified ID
            var beforeIndex = allMessages.FindIndex(m => m.Id == request.BeforeId);
            if (beforeIndex < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Message with before_id '{request.BeforeId}' not found"));
            }
            startIndex = beforeIndex + 1; // Start after the specified message (older messages)
            hasMoreAfter = true; // There are newer messages (at least the before_id message)
        }
        else if (!string.IsNullOrEmpty(request.AfterId))
        {
            // after_id: get messages NEWER than the specified ID
            var afterIndex = allMessages.FindIndex(m => m.Id == request.AfterId);
            if (afterIndex < 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Message with after_id '{request.AfterId}' not found"));
            }
            endIndex = afterIndex; // End before the specified message (newer messages only)
            hasMoreBefore = true; // There are older messages (at least the after_id message)
        }

        // Slice the messages based on calculated indices
        var filteredMessages = allMessages.Skip(startIndex).Take(endIndex - startIndex).ToList();

        // Check if there are more messages beyond the limit
        if (filteredMessages.Count > request.Limit)
        {
            hasMoreBefore = true;
            filteredMessages = filteredMessages.Take(request.Limit).ToList();
        }

        // Check if there are more older messages (either in allMessages or in the cloud)
        if (startIndex + filteredMessages.Count < allMessages.Count)
        {
            hasMoreBefore = true;
        }
        else if (moreOlderMessagesInCloud && endIndex == allMessages.Count)
        {
            // We're at the end of allMessages but cloud might have more
            hasMoreBefore = true;
        }

        // Convert to proto messages (keep newest-first order for response)
        var protoMessages = filteredMessages.Select(m => new Rpc.ContactChatMessage
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
            SenderId = m.SenderId,
        });

        return new GetContactMessagesResponse
        {
            Messages = { protoMessages },
            HasMoreBefore = hasMoreBefore,
            HasMoreAfter = hasMoreAfter,
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

    public override async Task<GetStartupConfigToRestoreResponse> GetStartupConfigToRestore(GetStartupConfigToRestoreRequest request, ServerCallContext context)
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
        var config = new StartupConfig
        {
            TickRate = _runnerService.TickRate,
            MaxConcurrentAssetTransfers = SessionAssetTransferer.OverrideMaxConcurrentTransfers ?? 4,
            AllowedUrlHosts = { allowedList },
            AutoSpawnItems = { _worldService.AutoSpawnItems.Select(uri => uri.ToString()) },
        };
        if (_engine.Cloud.UniverseID is not null)
        {
            config.UniverseId = _engine.Cloud.UniverseID;
        }
        if (_engine.UsernameOverride is not null)
        {
            config.UsernameOverride = _engine.UsernameOverride;
        }

        if (request.IncludeStartWorlds)
        {
            foreach (var session in _worldService.ListAll())
            {
                config.StartWorlds.Add(session.GenerateStartupParameters().ToProto());
            }
        }

        return new GetStartupConfigToRestoreResponse
        {
            StartupConfig = config
        };
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
}