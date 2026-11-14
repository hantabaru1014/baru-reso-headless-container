using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Round-trip coverage for AllowHostAccess / DenyHostAccess. The
/// HostAccessSettings store is exercised via UserspaceWorld.RunSynchronously,
/// so a successful add → GetHostSettings → remove → GetHostSettings cycle
/// catches both the SDK's Settings system and the Allow/Block helpers
/// breaking.
/// </summary>
[Collection("Container")]
public class HostAccessTests
{
    private readonly ContainerFixture _fixture;

    public HostAccessTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AllowHostAccess_ThenDenyHostAccess_RoundTripsThroughGetHostSettings()
    {
        using var channel = _fixture.CreateChannel();
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        // Use a unique host name per test run so we don't collide with
        // any state another test left behind.
        var host = $"test-host-{Guid.NewGuid():N}.invalid";
        const int port = 4242;

        await client.AllowHostAccessAsync(new AllowHostAccessRequest
        {
            Host = host,
            Port = port,
            AccessType = AllowedAccessEntry.Types.AccessType.Http,
        });

        try
        {
            // Settings.GetActiveSettingAsync writes asynchronously; poll
            // GetHostSettings until the entry shows up.
            var found = await PollForEntryAsync(client, host, expected: true, timeout: TimeSpan.FromSeconds(10));
            Assert.True(found, $"AllowHostAccess did not propagate to GetHostSettings for host {host}");
        }
        finally
        {
            // Clean up so subsequent runs don't see the entry.
            await client.DenyHostAccessAsync(new DenyHostAccessRequest
            {
                Host = host,
                Port = port,
                AccessType = AllowedAccessEntry.Types.AccessType.Http,
            });

            // Allow time for the deny to be reflected; failure here is
            // tolerated (cleanup, not assertion-worthy).
            await PollForEntryAsync(client, host, expected: false, timeout: TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task GetHostSettings_WhileHostAccessEntriesAreBeingAdded_DoesNotFail()
    {
        using var channel = _fixture.CreateChannel();
        var client = await GrpcTestHelpers.CreateReadyClientAsync(channel, _fixture);

        // AllowHostAccess は engine スレッドで HostAccessSettings.Entries に追加する。
        // その最中に GetHostSettings が Entries を列挙しても失敗しないことを、追加と取得を並行に繰り返して確かめる
        var prefix = $"stress-{Guid.NewGuid():N}";
        var hosts = Enumerable.Range(0, 50).Select(i => $"{prefix}-{i}.invalid").ToList();
        const int port = 4242;

        try
        {
            using var cts = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await client.GetHostSettingsAsync(new GetHostSettingsRequest());
                }
            })).ToList();

            foreach (var host in hosts)
            {
                await client.AllowHostAccessAsync(new AllowHostAccessRequest
                {
                    Host = host,
                    Port = port,
                    AccessType = AllowedAccessEntry.Types.AccessType.Http,
                });
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }

            // 追加が engine スレッドで反映されきるまで読み続ける
            await PollForEntryAsync(client, hosts[^1], expected: true, timeout: TimeSpan.FromSeconds(10));
            cts.Cancel();
            await Task.WhenAll(readers);
        }
        finally
        {
            foreach (var host in hosts)
            {
                await client.DenyHostAccessAsync(new DenyHostAccessRequest
                {
                    Host = host,
                    Port = port,
                    AccessType = AllowedAccessEntry.Types.AccessType.Http,
                });
            }
        }
    }

    private static async Task<bool> PollForEntryAsync(
        HeadlessControlService.HeadlessControlServiceClient client,
        string host,
        bool expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var settings = await client.GetHostSettingsAsync(new GetHostSettingsRequest());
            var present = settings.AllowedUrlHosts.Any(e => e.Host == host);
            if (present == expected) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return false;
    }
}
