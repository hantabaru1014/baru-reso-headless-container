using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the host-settings handlers. These exercise the get/update
/// round-trip for global server knobs (tick rate, max concurrent transfers,
/// username override, auto-spawn items) without touching the actual
/// transfer/engine plumbing — the assertion is purely that what you
/// PUT comes back from the GET.
/// </summary>
[Collection("Container")]
public class HostSettingsTests
{
    private readonly ContainerFixture _fixture;

    public HostSettingsTests(ContainerFixture fixture)
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
    public async Task GetHostSettings_ReturnsDefaults()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var response = await client.GetHostSettingsAsync(new GetHostSettingsRequest());

        Assert.NotNull(response);
        Assert.True(response.TickRate > 0, $"Default tick rate must be positive, got {response.TickRate}");
        Assert.True(response.MaxConcurrentAssetTransfers > 0,
            $"Default max concurrent asset transfers must be positive, got {response.MaxConcurrentAssetTransfers}");
        // allowed_url_hosts / auto_spawn_items default to empty collections
        // for an unconfigured guest-mode container, but proto repeated
        // fields are never null.
        Assert.NotNull(response.AllowedUrlHosts);
        Assert.NotNull(response.AutoSpawnItems);
    }

    [Fact]
    public async Task UpdateHostSettings_ChangesAreReflectedInGetHostSettings()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var before = await client.GetHostSettingsAsync(new GetHostSettingsRequest());
        var newMax = before.MaxConcurrentAssetTransfers + 1;

        await client.UpdateHostSettingsAsync(new UpdateHostSettingsRequest
        {
            MaxConcurrentAssetTransfers = newMax,
        });

        var after = await client.GetHostSettingsAsync(new GetHostSettingsRequest());
        Assert.Equal(newMax, after.MaxConcurrentAssetTransfers);

        // Restore so we don't leak shared state between collection tests.
        await client.UpdateHostSettingsAsync(new UpdateHostSettingsRequest
        {
            MaxConcurrentAssetTransfers = before.MaxConcurrentAssetTransfers,
        });
    }

    [Fact]
    public async Task UpdateHostSettings_AutoSpawnItems_RejectsInvalidUri()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var request = new UpdateHostSettingsRequest
        {
            UpdateAutoSpawnItems = true,
        };
        request.AutoSpawnItems.Add("not a uri");

        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
        {
            await client.UpdateHostSettingsAsync(request);
        });
        Assert.Equal(Grpc.Core.StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task GetStartupConfigToRestore_ReturnsCurrentTickRate()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = await ReadyClientAsync(channel);

        var settings = await client.GetHostSettingsAsync(new GetHostSettingsRequest());
        var response = await client.GetStartupConfigToRestoreAsync(
            new GetStartupConfigToRestoreRequest { IncludeStartWorlds = false });

        Assert.NotNull(response.StartupConfig);
        // tick_rate is optional in StartupConfig; when set, it must match
        // the live host setting. (It is set in the controller code in
        // GrpcControllerService.HostSettings.cs unconditionally.)
        Assert.Equal(settings.TickRate, response.StartupConfig.TickRate);
        Assert.Empty(response.StartupConfig.StartWorlds);
    }
}
