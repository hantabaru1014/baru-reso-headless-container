using FrooxEngine;
using SkyFrost.Base;

namespace Headless.Models;

public class RunningSession
{
    private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

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
    }

    private void OnUserJoined(FrooxEngine.User user)
    {
        if (user.IsLocalUser) return;

        LastJoinedUserId = user.UserID;
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
                else if (!cloud.HasPotentialAccess(record.OwnerId))
                {
                    // 保存権限のない公開ワールド
                    record.OwnerId = cloud.CurrentUserID;
                    record.RecordId = RecordHelper.GenerateRecordID();
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
}