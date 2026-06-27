using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Smoke test for <c>GetAbout</c>: verifies the controller returns both
/// the headless app version (read from the AppVersion file at build time)
/// and a non-empty Resonite engine version. Catches assembly-attribute
/// or DI wiring regressions early — they would otherwise only show up
/// when controllers complain about missing version info.
/// </summary>
[Collection("Container")]
public class GetAboutTests
{
    private readonly ContainerFixture _fixture;

    public GetAboutTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetAbout_ReturnsNonEmptyAppAndResoniteVersion()
    {
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Application startup did not complete in time");

        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        var response = await client.GetAboutAsync(new GetAboutRequest());

        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.AppVersion),
            "AppVersion should not be empty — Headless csproj sets InformationalVersion from the AppVersion file");
        Assert.False(string.IsNullOrEmpty(response.ResoniteVersion),
            "ResoniteVersion should not be empty — engine.VersionString must be populated by the time startup completes");
    }
}
