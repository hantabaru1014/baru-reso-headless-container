using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// End-to-end coverage for the world / session lifecycle gRPC handlers:
///   StartWorld → GetSession → UpdateSessionParameters → StopSession.
/// Complements <see cref="StartWorldTests"/> (which only asserts the
/// session count increases) by exercising the "do something with the
/// session you just started" path that controllers actually take.
/// </summary>
[Collection("Container")]
public class SessionLifecycleTests
{
    private readonly ContainerFixture _fixture;

    public SessionLifecycleTests(ContainerFixture fixture)
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
    public async Task GetSession_NonExistentId_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetSessionAsync(new GetSessionRequest { SessionId = "S-U-does-not-exist:nope" });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task StartWorld_WithoutLoadField_ReturnsInvalidArgument()
    {
        // The controller requires either load_world_url or load_world_preset_name.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.StartWorldAsync(new StartWorldRequest
            {
                Parameters = new WorldStartupParameters
                {
                    AccessLevel = AccessLevel.Private,
                }
            });
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetSession_ReturnsStartedSession()
    {
        // Happy path companion to GetSession_NonExistentId. Catches
        // ToProto() regressions when the Session message gains fields
        // or when RunningSession property bindings drift from the SDK.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            var resp = await client.GetSessionAsync(new GetSessionRequest { SessionId = sessionId });
            Assert.NotNull(resp.Session);
            Assert.Equal(sessionId, resp.Session.Id);
            Assert.NotNull(resp.Session.StartupParameters);
            // started_at is populated from RunningSession.StartedAt; must be
            // non-null even for guest sessions.
            Assert.NotNull(resp.Session.StartedAt);
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    [Fact]
    public async Task UpdateSessionParameters_MultipleScalarFields_RoundTripThroughGetSession()
    {
        // Exercises more of the engine setters than the Name-only happy
        // path: description / max_users / hide_from_public_listing /
        // save_on_exit / access_level. Any of those property names
        // changing in the SDK would surface here.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            const string newDescription = "description-by-test";
            const int newMax = 16;

            await client.UpdateSessionParametersAsync(new UpdateSessionParametersRequest
            {
                SessionId = sessionId,
                Description = newDescription,
                MaxUsers = newMax,
                HideFromPublicListing = true,
                SaveOnExit = false,
                AccessLevel = AccessLevel.Lan,
            });

            // Engine update thread is asynchronous w.r.t. the RPC reply.
            // Poll for description (the easiest-to-observe field).
            var ok = await PollUntilAsync(async () =>
            {
                var r = await client.GetSessionAsync(new GetSessionRequest { SessionId = sessionId });
                return r.Session.Description == newDescription
                       && r.Session.MaxUsers == newMax
                       && r.Session.HideFromPublicListing
                       && r.Session.AccessLevel == AccessLevel.Lan
                       && !r.Session.SaveOnExit;
            }, TimeSpan.FromSeconds(10));
            Assert.True(ok, "UpdateSessionParameters did not propagate to GetSession");
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    [Fact]
    public async Task UpdateSessionParameters_InvalidCloudVariablePath_ReturnsInvalidArgument()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
            {
                await client.UpdateSessionParametersAsync(new UpdateSessionParametersRequest
                {
                    SessionId = sessionId,
                    RoleCloudVariable = "@@@ definitely not a valid cloud variable path",
                });
            });
            Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    [Fact]
    public async Task UpdateSessionParameters_UpdatesNameAndReflectsInGetSession()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            const string newName = "renamed-by-test";
            await client.UpdateSessionParametersAsync(new UpdateSessionParametersRequest
            {
                SessionId = sessionId,
                Name = newName,
            });

            // The update is dispatched onto the engine update thread, so
            // a tiny grace period before reading back is reasonable.
            var observed = await PollForSessionNameAsync(client, sessionId, newName, TimeSpan.FromSeconds(5));
            Assert.Equal(newName, observed);
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    [Fact]
    public async Task StopSession_RemovesSessionFromListSessions()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var beforeStart = await client.ListSessionsAsync(new ListSessionsRequest());
        var sessionId = await StartGridSessionAsync(client);

        var afterStart = await client.ListSessionsAsync(new ListSessionsRequest());
        Assert.Equal(beforeStart.Sessions.Count + 1, afterStart.Sessions.Count);

        await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });

        // StopSession schedules cancellation; give the runtime a moment to
        // tear the session down and remove it from the running set.
        await WaitForSessionGoneAsync(client, sessionId, TimeSpan.FromSeconds(10));

        var afterStop = await client.ListSessionsAsync(new ListSessionsRequest());
        Assert.Equal(beforeStart.Sessions.Count, afterStop.Sessions.Count);
        Assert.DoesNotContain(afterStop.Sessions, s => s.Id == sessionId);
    }

    [Fact]
    public async Task StopSession_NonExistentId_DoesNotThrow()
    {
        // StopWorldAsync is a TryRemove → no-op if missing. The controller
        // surfaces that as success — controllers can fire-and-forget stop
        // without worrying about lost races.
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        await client.StopSessionAsync(new StopSessionRequest
        {
            SessionId = "S-U-does-not-exist:nope"
        });
    }

    private static async Task<string> StartGridSessionAsync(HeadlessControlService.HeadlessControlServiceClient client)
    {
        var response = await client.StartWorldAsync(new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private,
            }
        });
        Assert.NotNull(response.OpenedSession);
        Assert.False(string.IsNullOrEmpty(response.OpenedSession.Id));
        // Brief stabilization wait — Userspace.OpenWorld returns as soon
        // as the world transitions out of Initializing, but downstream
        // GetSession reads expect AttachEventBus to have been called.
        await Task.Delay(TimeSpan.FromSeconds(5));
        return response.OpenedSession.Id;
    }

    private static async Task<string?> PollForSessionNameAsync(
        HeadlessControlService.HeadlessControlServiceClient client,
        string sessionId,
        string expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            var resp = await client.GetSessionAsync(new GetSessionRequest { SessionId = sessionId });
            last = resp.Session.Name;
            if (last == expected) return last;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return last;
    }

    private static async Task<bool> PollUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return false;
    }

    private static async Task WaitForSessionGoneAsync(
        HeadlessControlService.HeadlessControlServiceClient client,
        string sessionId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var resp = await client.ListSessionsAsync(new ListSessionsRequest());
            if (resp.Sessions.All(s => s.Id != sessionId)) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
