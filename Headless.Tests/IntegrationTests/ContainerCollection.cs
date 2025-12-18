using Headless.Tests.Fixtures;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Defines a test collection that shares a single ContainerFixture instance
/// across all tests in the collection.
/// </summary>
[CollectionDefinition("Container")]
public class ContainerCollection : ICollectionFixture<ContainerFixture>
{
    // This class has no code, and is never created.
    // Its purpose is simply to be the place to apply [CollectionDefinition]
    // and all the ICollectionFixture<> interfaces.
}
