using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the <c>WatchHostEvents</c> server-streaming RPC. Exercises
/// the live subscription path end-to-end: subscribe → trigger an event
/// in the container → assert the streamed payload matches.
/// Also verifies that the resume token's strictly-greater-than semantics
/// hold across the wire (subscribing with the id of the last seen event
/// yields zero replay, then live events continue).
/// </summary>
[Collection("Container")]
public class WatchHostEventsTests
{
    private readonly ContainerFixture _fixture;

    public WatchHostEventsTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HeadlessControlService.HeadlessControlServiceClient> ReadyClientAsync(GrpcChannel channel)
    {
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Application startup did not complete in time");
        return new HeadlessControlService.HeadlessControlServiceClient(channel);
    }

    [Fact]
    public async Task WatchHostEvents_StartingAWorld_EmitsSessionStartedToLiveSubscriber()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var call = client.WatchHostEvents(
            new WatchHostEventsRequest { AfterEventId = "" },
            cancellationToken: cts.Token);

        // Give the server a tick to register the subscription before we
        // emit by starting a world. Without this, a race between Subscribe
        // and Emit could swallow the event we're trying to observe.
        await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);

        var startTask = client.StartWorldAsync(new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private,
            }
        }).ResponseAsync;

        string? startedSessionId = null;
        HostEvent? sessionStarted = null;
        try
        {
            while (await call.ResponseStream.MoveNext(cts.Token))
            {
                var ev = call.ResponseStream.Current;
                if (ev.PayloadCase == HostEvent.PayloadOneofCase.SessionStarted)
                {
                    sessionStarted = ev;
                    break;
                }
            }

            var startResponse = await startTask;
            startedSessionId = startResponse.OpenedSession.Id;
        }
        finally
        {
            if (startedSessionId is not null)
            {
                try
                {
                    await client.StopSessionAsync(new StopSessionRequest { SessionId = startedSessionId });
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        Assert.NotNull(sessionStarted);
        Assert.False(string.IsNullOrEmpty(sessionStarted!.Id));
        Assert.NotNull(sessionStarted.OccurredAt);
        Assert.Equal(startedSessionId, sessionStarted.SessionStarted.SessionId);
    }

    [Fact]
    public async Task WatchHostEvents_ResumeFromLastEventId_DoesNotReplayThatEvent()
    {
        // Strictly-greater-than semantics across the wire: subscribe live
        // (after_event_id=""), wait for an event, capture its id, then
        // open a fresh subscription with after_event_id=<that id> and
        // verify the first thing we observe is NOT the captured event.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        using var topCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        // Phase 1: trigger an event, capture its id.
        string capturedSessionId;
        string firstEventId;
        {
            using var call = client.WatchHostEvents(
                new WatchHostEventsRequest { AfterEventId = "" },
                cancellationToken: topCts.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(500), topCts.Token);

            var startResponse = await client.StartWorldAsync(new StartWorldRequest
            {
                Parameters = new WorldStartupParameters
                {
                    LoadWorldPresetName = "Grid",
                    AccessLevel = AccessLevel.Private,
                }
            });
            capturedSessionId = startResponse.OpenedSession.Id;

            HostEvent? first = null;
            while (await call.ResponseStream.MoveNext(topCts.Token))
            {
                var ev = call.ResponseStream.Current;
                if (ev.PayloadCase == HostEvent.PayloadOneofCase.SessionStarted
                    && ev.SessionStarted.SessionId == capturedSessionId)
                {
                    first = ev;
                    break;
                }
            }
            Assert.NotNull(first);
            firstEventId = first!.Id;
        }

        try
        {
            // Phase 2: resume from firstEventId. The captured SessionStarted
            // should NOT be re-delivered. Trigger a fresh event (StopSession
            // → SessionEnded) and verify that is what we see.
            using var resumeCall = client.WatchHostEvents(
                new WatchHostEventsRequest { AfterEventId = firstEventId },
                cancellationToken: topCts.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(500), topCts.Token);

            // Trigger a SessionEnded by stopping the world we started.
            await client.StopSessionAsync(new StopSessionRequest { SessionId = capturedSessionId });

            HostEvent? observed = null;
            while (await resumeCall.ResponseStream.MoveNext(topCts.Token))
            {
                var ev = resumeCall.ResponseStream.Current;
                Assert.NotEqual(firstEventId, ev.Id);
                if (ev.PayloadCase == HostEvent.PayloadOneofCase.SessionEnded
                    && ev.SessionEnded.SessionId == capturedSessionId)
                {
                    observed = ev;
                    break;
                }
            }
            Assert.NotNull(observed);
            Assert.True(string.CompareOrdinal(observed!.Id, firstEventId) > 0,
                "Resumed events must have ids strictly greater than the resume token");
        }
        catch
        {
            // Best-effort: if we crashed before the StopSession ran, make
            // sure we don't leak the running world into other tests.
            try
            {
                await client.StopSessionAsync(new StopSessionRequest { SessionId = capturedSessionId });
            }
            catch { }
            throw;
        }
    }
}
