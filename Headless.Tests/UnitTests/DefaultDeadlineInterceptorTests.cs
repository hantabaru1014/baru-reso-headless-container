using Grpc.Core;
using Grpc.Core.Interceptors;
using Headless.Tests.Helpers;

namespace Headless.Tests.UnitTests;

public class DefaultDeadlineInterceptorTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// 呼び出し時の CallOptions を記録するだけの CallInvoker
    /// </summary>
    private sealed class RecordingCallInvoker : CallInvoker
    {
        public CallOptions? LastOptions { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            LastOptions = options;
            return default!;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            LastOptions = options;
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult(default(TResponse)!),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            LastOptions = options;
            return null!;
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options)
            => throw new NotSupportedException();
    }

    private static readonly Marshaller<string> StringMarshaller = Marshallers.Create(
        s => System.Text.Encoding.UTF8.GetBytes(s),
        b => System.Text.Encoding.UTF8.GetString(b));

    private static Method<string, string> CreateMethod(MethodType type) =>
        new(type, "test.Service", "Call", StringMarshaller, StringMarshaller);

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    private readonly RecordingCallInvoker _inner = new();
    private readonly CallInvoker _invoker;

    public DefaultDeadlineInterceptorTests()
    {
        _invoker = _inner.Intercept(new DefaultDeadlineInterceptor(Deadline, new FixedTimeProvider(Now)));
    }

    [Fact]
    public void AsyncUnaryCall_WithoutDeadline_AddsDefaultDeadline()
    {
        _invoker.AsyncUnaryCall(CreateMethod(MethodType.Unary), null, new CallOptions(), "req");

        Assert.Equal((Now + Deadline).UtcDateTime, _inner.LastOptions?.Deadline);
    }

    [Fact]
    public void BlockingUnaryCall_WithoutDeadline_AddsDefaultDeadline()
    {
        _invoker.BlockingUnaryCall(CreateMethod(MethodType.Unary), null, new CallOptions(), "req");

        Assert.Equal((Now + Deadline).UtcDateTime, _inner.LastOptions?.Deadline);
    }

    [Fact]
    public void AsyncUnaryCall_WithExplicitDeadline_KeepsIt()
    {
        var explicitDeadline = Now.UtcDateTime.AddSeconds(5);

        _invoker.AsyncUnaryCall(CreateMethod(MethodType.Unary), null, new CallOptions(deadline: explicitDeadline), "req");

        Assert.Equal(explicitDeadline, _inner.LastOptions?.Deadline);
    }

    [Fact]
    public void AsyncServerStreamingCall_IsNotGivenDeadline()
    {
        _invoker.AsyncServerStreamingCall(CreateMethod(MethodType.ServerStreaming), null, new CallOptions(), "req");

        Assert.Null(_inner.LastOptions?.Deadline);
    }
}
