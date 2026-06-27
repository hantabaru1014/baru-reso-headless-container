using Elements.Core;
using FrooxEngine;
using Headless.Events;
using Headless.Rpc;
using Headless.Services;
using SkyFrost.Base;

namespace Headless.Models;

public class RunningSession
{
    private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
    private readonly object _linkBridgeLock = new();
    private HostEventBus? _eventBus;
    private WorldSavedHook? _worldSavedHook;
    private ResoniteLinkBridge? _linkBridge;

    internal Task? Handler { get; set; }

    private ExtendedWorldStartupParameters StartInfo { get; init; }

    public World Instance { get; init; }

    public CancellationTokenSource CancellationTokenSource { get; init; }

    public DateTimeOffset? IdleBeganAt { get; internal set; }

    public DateTimeOffset? LastSavedAt { get; private set; }

    public int LastUserCount { get; internal set; } = 0;

    public TimeSpan AutosaveInterval { get; set; }

    public bool AutoRecover { get; set; }

    /// <summary>
    /// the time after which an idle world should restart.
    /// </summary>
    public TimeSpan IdleRestartInterval { get; set; }

    /// <summary>
    /// Gets the absolute time after which a world should unconditionally restart.
    /// </summary>
    public TimeSpan ForceRestartInterval { get; set; }

    /// <summary>
    /// セッション開始時間
    /// </summary>
    public DateTimeOffset StartedAt => new DateTimeOffset(Instance.Time.LocalSessionBeginTime);

    /// <summary>
    /// HostUserを除く最後に入ったユーザのID
    /// </summary>
    public string? LastJoinedUserId { get; private set; }

    /// <summary>
    /// ワールド保存後にユーザがいたかどうか
    /// </summary>
    public bool IsDirty => Instance.UserCount > 1 || (LastJoinedUserId != null && (IdleBeganAt ?? DateTimeOffset.MinValue) > (LastSavedAt ?? DateTimeOffset.MinValue));

    /// <summary>
    /// Gets the uptime of the session.
    /// </summary>
    public TimeSpan TimeRunning => DateTimeOffset.UtcNow - StartedAt;

    /// <summary>
    /// Gets the time spent by the world in the current idle state.
    /// </summary>
    public TimeSpan TimeSpentIdle => IdleBeganAt is null
        ? TimeSpan.Zero
        : DateTimeOffset.UtcNow - IdleBeganAt.Value;

    /// <summary>
    /// Gets a value indicating whether the forced restart interval has elapsed.
    /// </summary>
    public bool HasForcedRestartIntervalElapsed => ForceRestartInterval > TimeSpan.Zero &&
                                                    TimeRunning > ForceRestartInterval;

    /// <summary>
    /// Gets a value indicating whether the autosave interval has elapsed.
    /// </summary>
    public bool HasAutosaveIntervalElapsed => AutosaveInterval > TimeSpan.Zero &&
                                               DateTimeOffset.UtcNow - (LastSavedAt != null ? LastSavedAt : StartedAt) > AutosaveInterval;

    /// <summary>
    /// Gets a value indicating whether the idle restart interval has elapsed.
    /// </summary>
    public bool HasIdleTimeElapsed => IdleRestartInterval > TimeSpan.Zero &&
                                        TimeSpentIdle > IdleRestartInterval;

    public RunningSession(ExtendedWorldStartupParameters startInfo, World worldInstance, CancellationTokenSource cancellationTokenSource)
    {
        StartInfo = startInfo;
        Instance = worldInstance;
        CancellationTokenSource = cancellationTokenSource;

        AutosaveInterval = TimeSpan.FromSeconds(startInfo.AutoSaveInterval);
        AutoRecover = startInfo.AutoRecover;
        IdleRestartInterval = TimeSpan.FromSeconds(startInfo.IdleRestartInterval);
        ForceRestartInterval = TimeSpan.FromSeconds(startInfo.ForcedRestartInterval);

        worldInstance.UserJoined += OnUserJoined;
        worldInstance.UserLeft += OnUserLeft;
    }

    /// <summary>
    /// Wire this session to a HostEventBus so engine UserJoined/UserLeft
    /// callbacks emit UserJoinedSession / UserLeftSession events, and so
    /// every save (including those initiated from inside the running
    /// world) emits WorldSaved. Must be called AFTER the session's
    /// SessionStarted event has been emitted — otherwise a user that
    /// joins between OpenWorld and SessionStarted would produce a join
    /// event with an id that sorts before the session-start id,
    /// confusing event-order consumers.
    /// </summary>
    internal void AttachEventBus(HostEventBus eventBus)
    {
        _eventBus = eventBus;
        _worldSavedHook = new WorldSavedHook(Instance, eventBus);
    }

    /// <summary>
    /// Detach the bus, unregister engine hooks. Called when the session
    /// is winding down so the bus does not receive late events for a
    /// world that is about to be destroyed.
    /// </summary>
    internal void DetachEventBus()
    {
        _worldSavedHook?.Dispose();
        _worldSavedHook = null;
        _eventBus = null;
    }

    /// <summary>
    /// 現在この session に紐付いている ResoniteLink bridge 上のクライアント数。
    /// </summary>
    public int ResoniteLinkClientsCount => _linkBridge?.ClientsCount ?? 0;

    /// <summary>
    /// ResoniteLink ブリッジを取得 (未生成なら作成)。
    /// </summary>
    public ResoniteLinkBridge GetOrCreateLinkBridge(ILogger logger)
    {
        if (_linkBridge is not null) return _linkBridge;
        lock (_linkBridgeLock)
        {
            _linkBridge ??= new ResoniteLinkBridge(Instance, logger);
            return _linkBridge;
        }
    }

    internal void DisposeLinkBridge()
    {
        ResoniteLinkBridge? bridge;
        lock (_linkBridgeLock)
        {
            bridge = _linkBridge;
            _linkBridge = null;
        }
        bridge?.Dispose();
    }

    private void OnUserJoined(FrooxEngine.User user)
    {
        if (user.IsLocalUser) return;

        LastJoinedUserId = user.UserID;

        if (user.IsHost || string.IsNullOrEmpty(user.UserID)) return;

        // The handler runs on the FrooxEngine update thread; any throw
        // here would propagate through FrooxEngine's multicast dispatcher
        // and skip the remaining UserJoined subscribers (and worse, could
        // destabilise the engine update tick). Trap and log instead.
        try
        {
            _eventBus?.Emit(new UserJoinedSession
            {
                SessionId = Instance.SessionId,
                UserId = user.UserID,
                UserName = user.UserName ?? "",
            });
        }
        catch (Exception ex)
        {
            UniLog.Warning($"HostEventBus.Emit(UserJoinedSession) threw: {ex}");
        }
    }

    private void OnUserLeft(FrooxEngine.User user)
    {
        if (user.IsLocalUser || user.IsHost || string.IsNullOrEmpty(user.UserID)) return;

        try
        {
            _eventBus?.Emit(new UserLeftSession
            {
                SessionId = Instance.SessionId,
                UserId = user.UserID,
                UserName = user.UserName ?? "",
            });
        }
        catch (Exception ex)
        {
            UniLog.Warning($"HostEventBus.Emit(UserLeftSession) threw: {ex}");
        }
    }

    public ExtendedWorldStartupParameters GenerateStartupParameters()
    {
        var info = Instance.GenerateSessionInfo();
        return new ExtendedWorldStartupParameters
        {
            IsEnabled = true,
            SessionName = info.Name,
            CustomSessionId = StartInfo.CustomSessionId,
            Description = info.Description,
            MaxUsers = info.MaximumUsers,
            AccessLevel = info.AccessLevel,
            UseCustomJoinVerifier = Instance.UseCustomJoinVerifier,
            HideFromPublicListing = info.HideFromListing,
            Tags = info.Tags.ToList(),
            MobileFriendly = Instance.MobileFriendly,
            LoadWorldURL = StartInfo.LoadWorldURL,
            LoadWorldPresetName = StartInfo.LoadWorldPresetName,
            OverrideCorrespondingWorldId = StartInfo.OverrideCorrespondingWorldId,
            ForcePort = StartInfo.ForcePort,
            KeepOriginalRoles = StartInfo.KeepOriginalRoles,
            DefaultUserRoles = StartInfo.DefaultUserRoles, // TODO: Instance.Permissions.DefaultUserPermissions から作るか決める
            RoleCloudVariable = Instance.Permissions.DefaultRoleCloudVariable,
            AllowUserCloudVariable = Instance.AllowUserCloudVariable,
            DenyUserCloudVariable = Instance.DenyUserCloudVariable,
            RequiredUserJoinCloudVariable = Instance.RequiredUserJoinCloudVariable,
            RequiredUserJoinCloudVariableDenyMessage = Instance.RequiredUserJoinCloudVariableDenyMessage,
            AwayKickMinutes = info.AwayKickEnabled ? info.AwayKickMinutes : -1,
            ParentSessionIds = Instance.ParentSessionIds.ToList(),
            AutoInviteUsernames = StartInfo.AutoInviteUsernames,
            InviteRequestHandlerUsernames = StartInfo.InviteRequestHandlerUsernames,
            AutoInviteMessage = StartInfo.AutoInviteMessage,
            SaveAsOwner = null,
            AutoRecover = AutoRecover,
            IdleRestartInterval = IdleRestartInterval.TotalSeconds,
            ForcedRestartInterval = ForceRestartInterval.TotalSeconds,
            SaveOnExit = Instance.SaveOnExit,
            AutoSaveInterval = AutosaveInterval.TotalSeconds,
            AutoSleep = !Instance.ForceFullUpdateCycle,
            WaitForLogin = StartInfo.WaitForLogin,
            JoinAllowedUserIds = StartInfo.JoinAllowedUserIds,
        };
    }

    public Task<bool> InviteUser(string userId) => Instance.Coroutines.StartTask(async () =>
    {
        Instance.AllowUserToJoin(userId);
        var userMessages = Instance.Engine.Cloud.Messages.GetUserMessages(userId);
        if (!await userMessages.SendMessage(await userMessages.CreateInviteMessage(Instance)))
        {
            return false;
        }
        return true;
    });

    public void AllowUserToJoin(string userId)
    {
        Instance.AllowUserToJoin(userId);
        StartInfo.JoinAllowedUserIds.Add(userId);
    }

    /// <summary>
    /// ワールドを保存中かどうか
    /// </summary>
    public bool IsWorldSaving => _saveLock.CurrentCount == 0;

    public async Task<bool> SaveWorld()
    {
        if (Userspace.ShouldSave(Instance) && !IsWorldSaving)
        {
            await _saveLock.WaitAsync();
            try
            {
                // TODO: 本来ならこのタイミングでサムネイルを撮影して world.CorrespondingRecord.ThumbnailURI に入れてる
                // クライアントがいるなら撮影してもらうか、最後のセッションサムネイルをセットしてもいいかも？
                // Memo: Userspace.SaveWorldAuto は サムネイル撮影 + SaveWorld + ExitWorld といった建付け
                var record = Instance.CorrespondingRecord;
                var cloud = Instance.Engine.Cloud;
                if (record is null)
                {
                    // Presetから作ったワールド
                    record = Instance.CreateNewRecord(cloud.CurrentUserID);
                    Instance.CorrespondingRecord = record;
                }
                var savedRecord = await Userspace.SaveWorld(Instance);
                LastSavedAt = DateTimeOffset.UtcNow;
                StartInfo.LoadWorldURL = savedRecord.GetUrl(cloud.Platform).ToString();
            }
            catch
            {
                return false;
            }
            finally
            {
                _saveLock.Release();
            }
            return true;
        }
        return false;
    }

    public async Task ExportWorldBinaryAsync(
        WorldBinaryFormat format,
        System.IO.Stream destination,
        bool includeVariants,
        int? brotliQuality,
        CancellationToken ct = default)
    {
        if (!Instance.IsAllowedToSaveWorld())
        {
            throw new InvalidOperationException("World is not allowed to be saved");
        }

        var graph = await Instance.Coroutines.StartTask(async () =>
        {
            await default(NextUpdate);
            // resonitepackage は SDK の PackageImporter が record.RecordType == "object" のみ受け付けるため
            // (FrooxEngine.PackageImporter.ImportPackage の "Currently only object packages are supported")、
            // RootSlot を Object として保存する。7zbson / brson は世界として開けるので World.SaveWorld() を使う。
            return format == WorldBinaryFormat.Resonitepackage
                ? Instance.RootSlot.SaveObject(DependencyHandling.CollectAssets)
                : Instance.SaveWorld();
        });
        if (graph is null)
        {
            throw new InvalidOperationException("Failed to capture world snapshot");
        }

        ct.ThrowIfCancellationRequested();

        switch (format)
        {
            case WorldBinaryFormat._7Zbson:
                DataTreeConverter.To7zBSON(graph.Root, destination);
                break;
            case WorldBinaryFormat.Brson:
                DataTreeConverter.ToBRSON(graph.Root, destination, brotliQuality ?? 9);
                break;
            case WorldBinaryFormat.Resonitepackage:
                // PackageExportable.Export と同じ作り方: フレッシュな object レコード。
                var worldName = !string.IsNullOrWhiteSpace(Instance.RawName) ? Instance.RawName : "World";
                var ownerId = Instance.Engine.Cloud.CurrentUserID ?? Instance.Engine.LocalDB.MachineID;
                var packageRecord = RecordHelper.CreateForObject<Record>(worldName, ownerId, null);
                await PackageCreator.BuildPackage(Instance.Engine, packageRecord, graph, destination, includeVariants);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported world binary format");
        }
    }

    public async Task<FrooxEngine.Store.Record?> SaveWorldCopy(string? ownerId)
    {
        if (!Instance.IsAllowedToSaveWorld()
            || IsWorldSaving
            || (ownerId != null && !Instance.Engine.Cloud.HasPotentialAccess(ownerId))) return null;

        await _saveLock.WaitAsync();
        try
        {
            string? originalOwnerId = null;
            var record = Instance.CorrespondingRecord;
            if (record != null)
            {
                originalOwnerId = record.OwnerId;
                record = record.Clone<FrooxEngine.Store.Record>();
                record.OwnerId = ownerId ?? Instance.GetCorrespondingOwnerId();
                record.RecordId = RecordHelper.GenerateRecordID();
                record.ClearRecordSpecificMetadata();
            }
            else
            {
                record = Instance.CreateNewRecord(ownerId);
            }

            var transfer = new RecordOwnerTransferer(Instance.Engine, record.OwnerId, originalOwnerId);
            var savedRecord = await Userspace.SaveWorld(Instance, record, transfer);
            LastSavedAt = DateTimeOffset.UtcNow;

            return savedRecord;
        }
        catch
        {
            return null;
        }
        finally
        {
            _saveLock.Release();
        }
    }
}