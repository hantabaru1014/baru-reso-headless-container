using System.Net;
using System.Net.Sockets;
using System.Text;
using Headless.Libs;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Tests.UnitTests;

/// <summary>
/// SkyFrost's ApiClient.RunRequest keeps a retryable response (429/5xx) undisposed with its body unread
/// while it retries, which pins the pooled connection. These tests reproduce that usage against a
/// single-connection pool and lock in that <see cref="RetryableResponseBufferingHandler"/> gives the
/// connection back, without buffering ordinary responses.
/// </summary>
public class RetryableResponseBufferingHandlerTests : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpListener _listener = new();
    private readonly string _baseUrl;
    private readonly HttpClient _client;

    public RetryableResponseBufferingHandlerTests()
    {
        _baseUrl = $"http://127.0.0.1:{GetFreePort()}/";
        _listener.Prefixes.Add(_baseUrl);
        _listener.Start();
        _ = ServeAsync();

        var pool = new SocketsHttpHandler { MaxConnectionsPerServer = 1 };
        _client = new HttpClient(new RetryableResponseBufferingHandler(pool, NullLogger.Instance));
    }

    public void Dispose()
    {
        _client.Dispose();
        _listener.Close();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(522)]
    public async Task RetryableResponse_LeftUndisposed_DoesNotHoldConnection(int statusCode)
    {
        var retryable = await SendLikeApiClientAsync($"status/{statusCode}");
        Assert.Equal(statusCode, (int)retryable.StatusCode);

        using var next = await SendLikeApiClientAsync("status/200");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);

        // RunRequest still reads the stale response once its retries are exhausted.
        Assert.Equal("body", await retryable.Content.ReadAsStringAsync());
        retryable.Dispose();
    }

    [Fact]
    public async Task OrdinaryResponse_IsNotBuffered()
    {
        using var unread = await SendLikeApiClientAsync("status/200");

        // Still streaming, so the only connection stays with the unread response.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendLikeApiClientAsync("status/200"));
    }

    [Fact]
    public async Task RetryableResponse_OverBufferLimit_IsDisposedAndThrows()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => SendLikeApiClientAsync("huge/503"));

        using var next = await SendLikeApiClientAsync("status/200");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    private async Task<HttpResponseMessage> SendLikeApiClientAsync(string path)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        return await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, _baseUrl + path),
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
    }

    private async Task ServeAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }

            var segments = context.Request.Url!.AbsolutePath.Trim('/').Split('/');
            var body = segments[0] == "huge" ? new byte[2 * 1024 * 1024] : Encoding.UTF8.GetBytes("body");
            try
            {
                context.Response.StatusCode = int.Parse(segments[1]);
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
            catch (Exception)
            {
                // The client gave up on this response.
                context.Response.Abort();
            }
        }
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
