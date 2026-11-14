using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;

namespace Headless.Tests.Helpers;

/// <summary>
/// 統合テスト用の gRPC チャネル。deadline 未指定の unary 呼び出しに既定の deadline を付ける。
/// </summary>
/// <remarks>
/// サーバー側で RPC が返らなくなると、deadline の無い呼び出しはテストセッションのタイムアウトまで待ち続け、
/// テストラン全体が Aborted になって原因のテストもコンテナログも残らない。
/// deadline を付けておけば、そのテストだけが DeadlineExceeded で失敗し、以降のテストとログ保存が続行される。
/// </remarks>
public sealed class TestGrpcChannel : ChannelBase, IDisposable
{
    // StartWorld (ワールドのロードを含む) が CI で十分に終わる長さ
    public static readonly TimeSpan DefaultUnaryDeadline = TimeSpan.FromMinutes(2);

    private readonly GrpcChannel _inner;
    private readonly DefaultDeadlineInterceptor _interceptor;

    public TestGrpcChannel(string address, TimeSpan? unaryDeadline = null) : base(address)
    {
        _inner = GrpcChannel.ForAddress(address);
        _interceptor = new DefaultDeadlineInterceptor(unaryDeadline ?? DefaultUnaryDeadline);
    }

    public override CallInvoker CreateCallInvoker() => _inner.CreateCallInvoker().Intercept(_interceptor);

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// deadline 未指定の unary 呼び出しに既定の deadline を付ける。ストリーミング呼び出しは寿命がテストごとに違うので対象外
/// </summary>
public sealed class DefaultDeadlineInterceptor : Interceptor
{
    private readonly TimeSpan _deadline;
    private readonly TimeProvider _timeProvider;

    public DefaultDeadlineInterceptor(TimeSpan deadline, TimeProvider? timeProvider = null)
    {
        _deadline = deadline;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithDefaultDeadline(context));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithDefaultDeadline(context));

    private ClientInterceptorContext<TRequest, TResponse> WithDefaultDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline is not null)
        {
            return context;
        }
        var options = context.Options.WithDeadline(_timeProvider.GetUtcNow().UtcDateTime + _deadline);
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }
}
