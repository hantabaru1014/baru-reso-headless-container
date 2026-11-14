using Grpc.Core;
using Grpc.Core.Interceptors;
using Headless.Libs;

namespace Headless.Services;

/// <summary>
/// RPC の処理中に対象のワールドが破棄された場合 (<see cref="WorldDestroyedException"/>) を、
/// "Exception was thrown by handler" (Unknown) ではなく Aborted としてクライアントに返す
/// </summary>
public sealed class WorldDestroyedExceptionInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (WorldDestroyedException ex)
        {
            throw ToRpcException(ex);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (WorldDestroyedException ex)
        {
            throw ToRpcException(ex);
        }
    }

    private static RpcException ToRpcException(WorldDestroyedException ex) =>
        new(new Status(StatusCode.Aborted, ex.Message, ex));
}
