using Grpc.Core;
using Headless.Libs;
using Headless.Services;

namespace Headless.Tests.UnitTests;

public class WorldDestroyedExceptionInterceptorTests
{
    private readonly WorldDestroyedExceptionInterceptor _interceptor = new();

    [Fact]
    public async Task UnaryServerHandler_WorldDestroyed_ReturnsAborted()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _interceptor.UnaryServerHandler<string, string>("req", null!, (_, _) => throw new WorldDestroyedException("w")));

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
        Assert.Contains("w was destroyed", ex.Status.Detail);
    }

    [Fact]
    public async Task UnaryServerHandler_OtherExceptions_PassThrough()
    {
        var original = new RpcException(new Status(StatusCode.InvalidArgument, "bad"));

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _interceptor.UnaryServerHandler<string, string>("req", null!, (_, _) => throw original));

        Assert.Same(original, ex);
    }

    [Fact]
    public async Task UnaryServerHandler_Success_ReturnsResponse()
    {
        var response = await _interceptor.UnaryServerHandler<string, string>("req", null!, (req, _) => Task.FromResult(req + "-ok"));

        Assert.Equal("req-ok", response);
    }

    [Fact]
    public async Task ServerStreamingServerHandler_WorldDestroyed_ReturnsAborted()
    {
        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            _interceptor.ServerStreamingServerHandler<string, string>("req", null!, null!, (_, _, _) => throw new WorldDestroyedException("w")));

        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
    }
}
