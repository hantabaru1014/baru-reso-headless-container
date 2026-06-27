namespace Headless.Events;

/// <summary>
/// Signals that a <see cref="HostEventBus"/> subscriber was forcibly
/// detached because it could not keep up with the broadcast rate. Surfaced
/// to the gRPC layer via channel completion so the server can return
/// RESOURCE_EXHAUSTED and the client knows to retry.
/// </summary>
public sealed class SubscriberDroppedException : Exception
{
    public SubscriberDroppedException(string message) : base(message)
    {
    }
}
