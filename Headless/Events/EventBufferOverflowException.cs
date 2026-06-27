namespace Headless.Events;

/// <summary>
/// Thrown by <see cref="HostEventBus.Subscribe"/> when the requested
/// <c>afterEventId</c> is older than the oldest event still held in the
/// ring buffer, meaning the client has missed events and must do a full
/// state resync.
/// </summary>
public sealed class EventBufferOverflowException : Exception
{
    public EventBufferOverflowException(string message) : base(message)
    {
    }
}
