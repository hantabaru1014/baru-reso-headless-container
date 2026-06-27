using Grpc.Core;
using Headless.Events;
using Headless.Rpc;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override async Task WatchHostEvents(
        WatchHostEventsRequest request,
        IServerStreamWriter<HostEvent> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;

        HostEventBus.Subscription sub;
        try
        {
            sub = _eventBus.Subscribe(request.AfterEventId);
        }
        catch (EventBufferOverflowException ex)
        {
            throw new RpcException(new Status(
                StatusCode.OutOfRange,
                "after_event_id is older than buffered events; full resync required",
                ex));
        }

        try
        {
            await foreach (var ev in sub.Reader.ReadAllAsync(ct))
            {
                await responseStream.WriteAsync(ev, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown / client disconnect
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            // Routine disconnects can surface as IOException / RpcException
            // from responseStream.WriteAsync after the client went away.
            // The cancellation token also fires in that case, so treating
            // any exception under ct.Cancelled as a clean shutdown keeps
            // the logs quiet.
            _ = ex;
        }
        catch (SubscriberDroppedException ex)
        {
            // Surface slow-subscriber drop as RESOURCE_EXHAUSTED so the
            // client can decide whether to retry with a higher tolerance
            // (or fall back to a full resync).
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "subscriber dropped: receive channel was full (too slow to keep up)",
                ex));
        }
        finally
        {
            sub.Dispose();
        }
    }
}
