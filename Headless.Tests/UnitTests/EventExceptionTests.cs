using Headless.Events;

namespace Headless.Tests.UnitTests;

/// <summary>
/// Both exceptions are minimal but are part of the public gRPC contract:
/// <see cref="EventBufferOverflowException"/> maps to OUT_OF_RANGE and
/// <see cref="SubscriberDroppedException"/> maps to RESOURCE_EXHAUSTED in
/// the streaming layer. Lock the message-pass-through and throwability
/// down so a future refactor (e.g. ctor signature change) is caught here
/// instead of as a regression in the controller.
/// </summary>
public class EventExceptionTests
{
    [Fact]
    public void EventBufferOverflowException_PreservesMessage()
    {
        var ex = new EventBufferOverflowException("stale token: 01J");
        Assert.Equal("stale token: 01J", ex.Message);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void EventBufferOverflowException_CanBeThrownAndCaught()
    {
        Action act = static () => throw new EventBufferOverflowException("x");
        Assert.Throws<EventBufferOverflowException>(act);
    }

    [Fact]
    public void SubscriberDroppedException_PreservesMessage()
    {
        var ex = new SubscriberDroppedException("channel full");
        Assert.Equal("channel full", ex.Message);
        Assert.IsAssignableFrom<Exception>(ex);
    }

    [Fact]
    public void SubscriberDroppedException_CanBeThrownAndCaught()
    {
        Action act = static () => throw new SubscriberDroppedException("x");
        Assert.Throws<SubscriberDroppedException>(act);
    }
}
