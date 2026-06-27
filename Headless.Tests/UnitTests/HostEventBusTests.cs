using Headless.Events;
using Headless.Rpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Tests.UnitTests;

public class HostEventBusTests
{
    private static HostEventBus NewBus(int capacity = HostEventBus.DefaultCapacity)
        => new HostEventBus(capacity, NullLogger<HostEventBus>.Instance);

    private static List<HostEvent> DrainBuffered(HostEventBus.Subscription sub)
    {
        var result = new List<HostEvent>();
        while (sub.Reader.TryRead(out var ev))
        {
            result.Add(ev);
        }
        return result;
    }

    [Fact]
    public void Emit_AssignsMonotonicallyIncreasingIds()
    {
        var bus = NewBus();

        using var sub = bus.Subscribe(afterEventId: "");
        bus.Emit(new SessionStarted { SessionId = "a", SessionName = "A" });
        bus.Emit(new SessionEnded { SessionId = "a" });
        bus.Emit(new WorldSaved { SessionId = "a" });

        var events = DrainBuffered(sub);
        Assert.Equal(3, events.Count);
        Assert.True(string.CompareOrdinal(events[0].Id, events[1].Id) < 0,
            $"id[0]={events[0].Id} must be < id[1]={events[1].Id}");
        Assert.True(string.CompareOrdinal(events[1].Id, events[2].Id) < 0,
            $"id[1]={events[1].Id} must be < id[2]={events[2].Id}");
    }

    [Fact]
    public async Task Subscribe_WithEmptyAfterId_ReceivesOnlyFutureEvents()
    {
        var bus = NewBus();
        bus.Emit(new SessionStarted { SessionId = "s1", SessionName = "first" });
        bus.Emit(new SessionStarted { SessionId = "s2", SessionName = "second" });

        using var sub = bus.Subscribe(afterEventId: "");
        Assert.False(sub.Reader.TryRead(out _), "live-only subscriber should not see backlog");

        bus.Emit(new SessionStarted { SessionId = "s3", SessionName = "third" });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ev = await sub.Reader.ReadAsync(cts.Token);
        Assert.Equal("s3", ev.SessionStarted.SessionId);
        Assert.False(sub.Reader.TryRead(out _), "no further events expected");
    }

    [Fact]
    public void Subscribe_WithAfterId_ReplaysBackloggedEvents()
    {
        var bus = NewBus();
        bus.Emit(new SessionStarted { SessionId = "s1", SessionName = "first" });

        // We need to discover an existing event's id to use as the resume
        // token. Snoop via a fresh live subscriber that catches the next
        // emit; that emit's id becomes our resume point. (The snoop is
        // disposed once we have what we need.)
        string resumeAfterId;
        {
            using var snoop = bus.Subscribe(afterEventId: "");
            bus.Emit(new SessionStarted { SessionId = "s2", SessionName = "second" });
            snoop.Reader.TryRead(out var ev);
            resumeAfterId = ev!.Id;
        }
        bus.Emit(new SessionStarted { SessionId = "s3", SessionName = "third" });

        using var sub = bus.Subscribe(afterEventId: resumeAfterId);

        var replayed = DrainBuffered(sub);
        Assert.Single(replayed);
        Assert.Equal("s3", replayed[0].SessionStarted.SessionId);

        bus.Emit(new SessionStarted { SessionId = "s4", SessionName = "fourth" });

        Assert.True(sub.Reader.TryRead(out var live));
        Assert.Equal("s4", live!.SessionStarted.SessionId);
    }

    [Fact]
    public void Subscribe_AfterIdTooOld_ThrowsBufferOverflow()
    {
        var bus = NewBus(capacity: 3);
        // Capture an id we'll later use as a stale resume token: the first
        // event's id, then push past capacity to trim it.
        string staleId;
        {
            using var snoop = bus.Subscribe(afterEventId: "");
            bus.Emit(new SessionStarted { SessionId = "s0", SessionName = "n0" });
            snoop.Reader.TryRead(out var first);
            staleId = first!.Id;
        }

        // Push 5 more so the buffer holds only the latest 3 (s2..s5);
        // s0 (=staleId) and s1 have been trimmed.
        for (var i = 1; i < 6; i++)
        {
            bus.Emit(new SessionStarted { SessionId = $"s{i}", SessionName = $"n{i}" });
        }

        Assert.Throws<EventBufferOverflowException>(() => bus.Subscribe(afterEventId: staleId));
    }

    [Fact]
    public void RingBuffer_TrimsOldEventsAtCapacity_ResumeFromLastTrimmedIsSafe()
    {
        var bus = NewBus(capacity: 3);

        // Capture the id of what will become the last-trimmed event.
        string lastTrimmedId;
        {
            using var snoop = bus.Subscribe(afterEventId: "");
            bus.Emit(new SessionStarted { SessionId = "s0", SessionName = "n0" });
            bus.Emit(new SessionStarted { SessionId = "s1", SessionName = "n1" });
            snoop.Reader.TryRead(out _);             // s0
            snoop.Reader.TryRead(out var snd);       // s1
            lastTrimmedId = snd!.Id;                 // will be the one trimmed last
        }

        // Push 3 more so s0, s1 are trimmed (capacity=3 holds s2,s3,s4).
        bus.Emit(new SessionStarted { SessionId = "s2", SessionName = "n2" });
        bus.Emit(new SessionStarted { SessionId = "s3", SessionName = "n3" });
        bus.Emit(new SessionStarted { SessionId = "s4", SessionName = "n4" });

        // afterEventId = s1 (== last-trimmed) is the edge case: client
        // already saw s1 and wants everything strictly after it. The bus
        // must NOT raise overflow here.
        using var sub = bus.Subscribe(afterEventId: lastTrimmedId);
        var replayed = DrainBuffered(sub);
        Assert.Equal(3, replayed.Count);
        Assert.Equal("s2", replayed[0].SessionStarted.SessionId);
        Assert.Equal("s3", replayed[1].SessionStarted.SessionId);
        Assert.Equal("s4", replayed[2].SessionStarted.SessionId);
    }

    [Fact]
    public async Task Dispose_UnregistersSubscriber()
    {
        var bus = NewBus();
        var sub = bus.Subscribe(afterEventId: "");
        var reader = sub.Reader;
        sub.Dispose();

        // After dispose, the channel should be completed (gracefully)
        // and no further events should arrive.
        bus.Emit(new SessionStarted { SessionId = "ignored", SessionName = "x" });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var count = 0;
        await foreach (var _ in reader.ReadAllAsync(cts.Token))
        {
            count++;
        }
        Assert.Equal(0, count);

        // Calling Dispose again should be a no-op.
        sub.Dispose();
    }

    [Fact]
    public async Task SlowSubscriber_DropsAndFails()
    {
        // Channel capacity == buffer capacity == 2. We subscribe but
        // never read, then emit 3 events. The 3rd TryWrite returns
        // false → subscriber is dropped with InvalidOperationException.
        var bus = NewBus(capacity: 2);
        using var sub = bus.Subscribe(afterEventId: "");

        bus.Emit(new SessionStarted { SessionId = "s1", SessionName = "1" });
        bus.Emit(new SessionStarted { SessionId = "s2", SessionName = "2" });
        bus.Emit(new SessionStarted { SessionId = "s3", SessionName = "3" });

        Assert.True(sub.Reader.TryRead(out _));
        Assert.True(sub.Reader.TryRead(out _));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in sub.Reader.ReadAllAsync(cts.Token))
            {
                // should not yield
            }
        });
        Assert.IsType<SubscriberDroppedException>(ex);
    }
}
