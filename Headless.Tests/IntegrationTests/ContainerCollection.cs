using Headless.Tests.Fixtures;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Defines a test collection that shares a single ContainerFixture instance
/// across all tests in the collection.
///
/// Coverage note: every gRPC endpoint defined in
/// <c>proto/headless/v1/headless.proto</c> has at least one test in this
/// collection, with one deliberate exception — <c>Shutdown</c>. Shutdown
/// terminates the container and would cause every subsequent test in the
/// shared fixture to fail; if you need to exercise it, do so in a one-off
/// fixture that does not participate in the "Container" collection.
/// </summary>
[CollectionDefinition("Container")]
public class ContainerCollection : ICollectionFixture<ContainerFixture>
{
    // This class has no code, and is never created.
    // Its purpose is simply to be the place to apply [CollectionDefinition]
    // and all the ICollectionFixture<> interfaces.
}
