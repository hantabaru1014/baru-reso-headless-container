using Elements.Core;
using FrooxEngine;
using Headless.Events;
using Headless.Extensions;
using Headless.Rpc;

namespace Headless.Models;

/// <summary>
/// Subscribes to the world's session-parameter sync members and emits a
/// <see cref="SessionParametersChanged"/> snapshot whenever any of them
/// changes. Coalesces bursts with a short debounce so a single user-driven
/// edit doesn't fan out into N events.
///
/// Non-sync world fields (SaveOnExit / IdleRestartInterval /
/// ForceFullUpdateCycle on World itself) can't fire Changed, so the only
/// path that observes them is an explicit <see cref="RequestEmit"/> call
/// from whoever applied the change (the gRPC UpdateSessionParameters
/// handler is the in-tree caller).
/// </summary>
internal sealed class SessionParametersChangedHook : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(500);

    private readonly RunningSession _session;
    private readonly HostEventBus _eventBus;
    private readonly TimeSpan _debounce;
    private readonly Timer _timer;
    private readonly List<IChangeable> _subscriptions = new();
    private int _disposed;

    internal SessionParametersChangedHook(RunningSession session, HostEventBus eventBus)
        : this(session, eventBus, DefaultDebounce)
    {
    }

    internal SessionParametersChangedHook(RunningSession session, HostEventBus eventBus, TimeSpan debounce)
    {
        _session = session;
        _eventBus = eventBus;
        _debounce = debounce;
        _timer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        var cfg = session.Instance.Configuration;
        // 値を持つ Sync<T> 系。Configuration の Sync field を直接購読する
        Subscribe(cfg.WorldName);
        Subscribe(cfg.WorldDescription);
        Subscribe(cfg.AccessLevel);
        Subscribe(cfg.MaxUsers);
        Subscribe(cfg.HideFromListing);
        Subscribe(cfg.AwayKickEnabled);
        Subscribe(cfg.AwayKickMinutes);
        Subscribe(cfg.AutoSaveEnabled);
        Subscribe(cfg.AutoSaveInterval);
        // IList 系
        Subscribe(cfg.WorldTags);
    }

    /// <summary>
    /// Manually arm the debounce. Used by callers that mutate non-sync
    /// world fields and so wouldn't otherwise trigger an event.
    /// </summary>
    public void RequestEmit() => ScheduleEmit();

    private void Subscribe(IChangeable target)
    {
        target.Changed += OnChanged;
        _subscriptions.Add(target);
    }

    private void OnChanged(IChangeable _) => ScheduleEmit();

    private void ScheduleEmit()
    {
        if (_disposed != 0) return;
        // 既存タイマがあれば延長されて 1 回だけ発火する (System.Threading.Timer 仕様)
        _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
    }

    private void OnDebounceElapsed(object? _)
    {
        if (_disposed != 0) return;
        try
        {
            var snapshot = _session.ToProto();
            _eventBus.Emit(new SessionParametersChanged
            {
                SessionId = snapshot.Id,
                Session = snapshot,
            });
        }
        catch (Exception ex)
        {
            UniLog.Warning($"HostEventBus.Emit(SessionParametersChanged) threw: {ex}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
        foreach (var sub in _subscriptions)
        {
            sub.Changed -= OnChanged;
        }
        _subscriptions.Clear();
    }
}
