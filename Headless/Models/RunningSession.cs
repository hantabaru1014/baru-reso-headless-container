using FrooxEngine;
using SkyFrost.Base;

namespace Headless.Models;

public class RunningSession
{
    private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

    internal Task? Handler { get; set; }

    public WorldStartupParameters StartInfo { get; init; }

    public World Instance { get; init; }

    public CancellationTokenSource CancellationTokenSource { get; init; }

    public DateTimeOffset? IdleBeganAt { get; internal set; }

    public DateTimeOffset? LastSavedAt { get; private set; }

    public int LastUserCount { get; internal set; } = 0;

    public TimeSpan AutosaveInterval
    {
        get => TimeSpan.FromSeconds(StartInfo.AutoSaveInterval);
        set => StartInfo.AutoSaveInterval = value.TotalSeconds;
    }

    /// <summary>
    /// the time after which an idle world should restart.
    /// </summary>
    public TimeSpan IdleRestartInterval
    {
        get => TimeSpan.FromSeconds(StartInfo.IdleRestartInterval);
        set => StartInfo.IdleRestartInterval = value.TotalSeconds;
    }

    /// <summary>
    /// Gets the absolute time after which a world should unconditionally restart.
    /// </summary>
    public TimeSpan ForceRestartInterval
    {
        get => TimeSpan.FromSeconds(StartInfo.ForcedRestartInterval);
        set => StartInfo.ForcedRestartInterval = value.TotalSeconds;
    }

    public DateTimeOffset StartedAt => new DateTimeOffset(Instance.Time.LocalSessionBeginTime);

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

    public RunningSession(WorldStartupParameters startInfo, World worldInstance, CancellationTokenSource cancellationTokenSource)
    {
        StartInfo = startInfo;
        Instance = worldInstance;
        CancellationTokenSource = cancellationTokenSource;
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