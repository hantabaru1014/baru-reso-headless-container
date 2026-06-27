using Headless.Configuration;

namespace Headless.Tests.UnitTests;

/// <summary>
/// <see cref="ApplicationConfig"/> is bound from configuration at startup.
/// A silent rename of one of its positional members (record syntax) would
/// break that binding — these tests lock the defaults and the with-expression
/// behavior so such a change shows up in a failing unit test rather than a
/// production misconfig.
/// </summary>
public class ApplicationConfigTests
{
    [Fact]
    public void DefaultConstructor_AppliesDocumentedDefaults()
    {
        var config = new ApplicationConfig();

        Assert.Equal("http://0.0.0.0:5000", config.RpcHostUrl);
        Assert.Equal(Directory.GetCurrentDirectory(), config.DataDirectoryPath);
        Assert.Null(config.HeadlessUserCredential);
        Assert.Null(config.HeadlessUserPassword);
        Assert.Null(config.BackgroundWorkers);
        Assert.Null(config.PriorityWorkers);
        Assert.Equal(180, config.ShutdownTimeoutSeconds);
    }

    [Fact]
    public void PositionalConstructor_AppliesProvidedValues()
    {
        var config = new ApplicationConfig(
            RpcHostUrl: "http://127.0.0.1:6000",
            DataDirectoryPath: "/data",
            HeadlessUserCredential: "u",
            HeadlessUserPassword: "p",
            BackgroundWorkers: 4,
            PriorityWorkers: 2,
            ShutdownTimeoutSeconds: 30);

        Assert.Equal("http://127.0.0.1:6000", config.RpcHostUrl);
        Assert.Equal("/data", config.DataDirectoryPath);
        Assert.Equal("u", config.HeadlessUserCredential);
        Assert.Equal("p", config.HeadlessUserPassword);
        Assert.Equal(4, config.BackgroundWorkers);
        Assert.Equal(2, config.PriorityWorkers);
        Assert.Equal(30, config.ShutdownTimeoutSeconds);
    }

    [Fact]
    public void WithExpression_OverridesIndividualMember()
    {
        var baseConfig = new ApplicationConfig();
        var overridden = baseConfig with { ShutdownTimeoutSeconds = 5 };

        Assert.Equal(5, overridden.ShutdownTimeoutSeconds);
        // Other members preserved.
        Assert.Equal(baseConfig.RpcHostUrl, overridden.RpcHostUrl);
        Assert.Equal(baseConfig.DataDirectoryPath, overridden.DataDirectoryPath);
        // Original untouched.
        Assert.Equal(180, baseConfig.ShutdownTimeoutSeconds);
    }

    [Fact]
    public void Equality_RecordsWithSameMembersAreEqual()
    {
        var a = new ApplicationConfig("http://x", "/y", "u", "p", 1, 1, 60);
        var b = new ApplicationConfig("http://x", "/y", "u", "p", 1, 1, 60);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
