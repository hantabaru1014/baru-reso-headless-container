using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Headless.Rpc;
using Microsoft.Extensions.Logging;
using NUlid;
using NUlid.Rng;

namespace Headless.Events;

/// <summary>
/// In-process pub/sub for <see cref="HostEvent"/>s with a bounded ring
/// buffer that allows freshly-(re)connected subscribers to resume from a
/// recently-seen id.
///
/// Lifetime: the container hosts a single instance of this bus
/// (registered as a singleton). Event ids are ULIDs — monotonically
/// increasing AND globally unique across container restarts, so a
/// controller's persisted resume token stays valid after either side
/// restarts.
/// </summary>
public sealed class HostEventBus
{
    /// <summary>
    /// Subscriber handle. Hold one of these for each active client and
    /// dispose it to unregister.
    /// </summary>
    public sealed class Subscription : IDisposable
    {
        private readonly HostEventBus _bus;
        private readonly Channel<HostEvent> _channel;
        private int _disposed;

        internal Subscription(HostEventBus bus, Channel<HostEvent> channel)
        {
            _bus = bus;
            _channel = channel;
        }

        public ChannelReader<HostEvent> Reader => _channel.Reader;

        internal ChannelWriter<HostEvent> Writer => _channel.Writer;

        /// <summary>
        /// Detach this subscriber. Pass a non-null <paramref name="reason"/>
        /// to surface the cause to the reader as a channel-completion
        /// exception (used internally when a slow subscriber is dropped);
        /// pass null for a graceful close.
        /// </summary>
        public void Dispose() => Dispose(null);

        internal void Dispose(Exception? reason)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _bus.Unsubscribe(this);
            _channel.Writer.TryComplete(reason);
        }
    }

    /// <summary>Default ring-buffer capacity if none is provided.</summary>
    public const int DefaultCapacity = 1000;

    private readonly int _capacity;
    private readonly ILogger<HostEventBus> _logger;
    private readonly MonotonicUlidRng _rng = new();
    private readonly object _lock = new();
    private readonly LinkedList<HostEvent> _buffer = new();
    private readonly List<Subscription> _subscribers = new();

    /// <summary>
    /// Highest id we have evicted from the buffer (the most recent trim).
    /// Initialised to the empty string which sorts before every ULID, so
    /// the overflow check is a single lexicographic compare with no null
    /// branch.
    /// </summary>
    private string _lastTrimmedId = "";

    public HostEventBus(ILogger<HostEventBus> logger) : this(DefaultCapacity, logger)
    {
    }

    public HostEventBus(int capacity, ILogger<HostEventBus> logger)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to the live event stream.
    /// </summary>
    /// <param name="afterEventId">
    /// Empty string (default) requests live-only delivery. Otherwise,
    /// events with id &gt; <paramref name="afterEventId"/> (lexicographic
    /// / ULID order) that are still in the ring buffer are replayed
    /// first, then live events follow.
    /// </param>
    /// <exception cref="EventBufferOverflowException">
    /// Raised when <paramref name="afterEventId"/> is non-empty and one or
    /// more events the caller would expect to replay have already been
    /// trimmed from the buffer.
    /// </exception>
    public Subscription Subscribe(string afterEventId)
    {
        // Channel capacity == ring buffer capacity, so a full replay
        // always fits and slow-subscriber detection kicks in only once
        // the receiver has fallen behind by more than the buffer size.
        var channel = Channel.CreateBounded<HostEvent>(new BoundedChannelOptions(_capacity)
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var subscription = new Subscription(this, channel);

        // Snapshot the replay slice under the lock; do the TryWrite-fan
        // outside so a large backlog cannot stall in-flight Emit calls
        // (and, in particular, cannot block the engine update thread that
        // calls Emit from world UserJoined/UserLeft handlers).
        HostEvent[]? replay = null;
        lock (_lock)
        {
            if (!string.IsNullOrEmpty(afterEventId))
            {
                if (string.CompareOrdinal(afterEventId, _lastTrimmedId) < 0)
                {
                    throw new EventBufferOverflowException(
                        $"afterEventId={afterEventId} is older than the most recently trimmed event (id={_lastTrimmedId}); full resync required");
                }

                var matched = new List<HostEvent>(_buffer.Count);
                foreach (var ev in _buffer)
                {
                    if (string.CompareOrdinal(ev.Id, afterEventId) > 0)
                    {
                        matched.Add(ev);
                    }
                }
                replay = matched.Count > 0 ? matched.ToArray() : null;
            }

            _subscribers.Add(subscription);
        }

        if (replay is not null)
        {
            foreach (var ev in replay)
            {
                subscription.Writer.TryWrite(ev);
            }
        }

        return subscription;
    }

    public void Emit(SessionStarted payload)
    {
        EmitInternal(ev => ev.SessionStarted = payload);
    }

    public void Emit(SessionEnded payload)
    {
        EmitInternal(ev => ev.SessionEnded = payload);
    }

    public void Emit(UserJoinedSession payload)
    {
        EmitInternal(ev => ev.UserJoinedSession = payload);
    }

    public void Emit(UserLeftSession payload)
    {
        EmitInternal(ev => ev.UserLeftSession = payload);
    }

    public void Emit(WorldSaved payload)
    {
        EmitInternal(ev => ev.WorldSaved = payload);
    }

    private void EmitInternal(Action<HostEvent> setPayload)
    {
        // Build the event outside the lock — ULID generation +
        // ToString() + Timestamp.FromDateTimeOffset() do not touch shared
        // state. NUlid's MonotonicUlidRng is thread-safe.
        var now = DateTimeOffset.UtcNow;
        var ev = new HostEvent
        {
            Id = Ulid.NewUlid(now, _rng).ToString(),
            OccurredAt = Timestamp.FromDateTimeOffset(now),
        };
        setPayload(ev);

        // Snapshot subscribers under the lock; fanout outside it so a
        // slow ChannelWriter.TryWrite does not serialise other emits.
        Subscription[] subscribers;
        lock (_lock)
        {
            _buffer.AddLast(ev);
            while (_buffer.Count > _capacity)
            {
                _lastTrimmedId = _buffer.First!.Value.Id;
                _buffer.RemoveFirst();
            }

            subscribers = _subscribers.ToArray();
        }

        List<Subscription>? failed = null;
        foreach (var sub in subscribers)
        {
            if (!sub.Writer.TryWrite(ev))
            {
                // TryWrite returning false against an already-completed
                // channel (i.e. the subscriber Disposed gracefully while
                // we were holding the snapshot) is NOT a slow drop —
                // skip the drop-and-log path in that case.
                if (sub.Reader.Completion.IsCompleted) continue;

                failed ??= new List<Subscription>();
                failed.Add(sub);
            }
        }

        if (failed is null) return;

        foreach (var sub in failed)
        {
            _logger.LogWarning("Dropping slow HostEventBus subscriber (channel full)");
            sub.Dispose(new SubscriberDroppedException(
                "Subscriber dropped because its receive channel was full (too slow)"));
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_lock)
        {
            _subscribers.Remove(subscription);
        }
    }
}
