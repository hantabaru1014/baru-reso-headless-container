using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for verifying container startup and engine initialization.
/// </summary>
[Collection("Container")]
public class ContainerStartupTests
{
    private readonly ContainerFixture _fixture;

    public ContainerStartupTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Container_ShouldLogEngineReady_WithinTimeout()
    {
        // Act
        var result = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));

        // Assert
        Assert.True(result,
            "Engine Ready! message was not found in container logs within 5 minutes. " +
            "This may indicate the container failed to start or the Resonite engine initialization failed.");
    }
}
