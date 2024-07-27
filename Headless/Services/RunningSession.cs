using FrooxEngine;
using SkyFrost.Base;

namespace Headless.Services;

public record RunningSession
(
    WorldStartupParameters StartInfo,
    World WorldInstance,
    CancellationTokenSource CancellationTokenSource,
    DateTimeOffset? IdleBeginTime = default,
    DateTimeOffset LastSaveTime = default,
    int LastUserCount = 0
)
{
    public Task? Handler { get; internal set; }

    /// <summary>
    /// Gets the interval at which the world should automatically save.
    /// </summary>
    public TimeSpan AutosaveInterval { get; } = TimeSpan.FromSeconds(StartInfo.AutoSaveInterval);

    /// <summary>
    /// Gets the time after which an idle world should restart.
    /// </summary>
    public TimeSpan IdleRestartInterval { get; } = TimeSpan.FromSeconds(StartInfo.IdleRestartInterval);

    /// <summary>
    /// Gets the absolute time after which a world should unconditionally restart.
    /// </summary>
    public TimeSpan ForceRestartInterval { get; } = TimeSpan.FromSeconds(StartInfo.ForcedRestartInterval);

    /// <summary>
    /// Gets the uptime of the session.
    /// </summary>
    public TimeSpan TimeRunning => DateTimeOffset.UtcNow - WorldInstance.Time.LocalSessionBeginTime;

    /// <summary>
    /// Gets the time elapsed since the last time the session saved.
    /// </summary>
    public TimeSpan TimeSinceLastSave => DateTimeOffset.UtcNow - this.LastSaveTime;

    /// <summary>
    /// Gets the time spent by the world in the current idle state.
    /// </summary>
    public TimeSpan TimeSpentIdle => this.IdleBeginTime is null
        ? TimeSpan.Zero
        : DateTimeOffset.UtcNow - this.IdleBeginTime.Value;

    /// <summary>
    /// Gets a value indicating whether the forced restart interval has elapsed.
    /// </summary>
    public bool HasForcedRestartIntervalElapsed => this.ForceRestartInterval > TimeSpan.Zero &&
                                                    this.TimeRunning > this.ForceRestartInterval;

    /// <summary>
    /// Gets a value indicating whether the autosave interval has elapsed.
    /// </summary>
    public bool HasAutosaveIntervalElapsed => this.AutosaveInterval > TimeSpan.Zero &&
                                                this.TimeSinceLastSave > this.AutosaveInterval;

    /// <summary>
    /// Gets a value indicating whether the idle restart interval has elapsed.
    /// </summary>
    public bool HasIdleTimeElapsed => this.IdleRestartInterval > TimeSpan.Zero &&
                                        this.TimeSpentIdle > this.IdleRestartInterval;

    /// <summary>
    /// Gets the last time at which the world was successfully saved and synchronized.
    /// </summary>
    public DateTimeOffset LastSaveTime { get; init; } = LastSaveTime == default
        ? DateTimeOffset.UtcNow
        : LastSaveTime;

    public Task<bool> InviteUser(string userId) => WorldInstance.Coroutines.StartTask(async () =>
    {
        WorldInstance.AllowUserToJoin(userId);
        var userMessages = WorldInstance.Engine.Cloud.Messages.GetUserMessages(userId);
        if (!await userMessages.SendMessage(await userMessages.CreateInviteMessage(WorldInstance)))
        {
            return false;
        }
        return true;
    });
}