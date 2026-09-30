using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Libs;

/// <summary>
/// SkyFrost の ApiClient.RunRequest は ResponseHeadersRead でレスポンスを受け取り、リトライ対象のステータスだった場合は
/// body を読まず Dispose もしないまま次の試行へ進む。body が未読のレスポンスは接続をプールへ返さないため、
/// finalizer が走るまで MaxConnectionsPerServer (16) の枠を占有し続け、枠が尽きると以降の API リクエストが
/// 接続待ちのまま全てタイムアウトする。
/// リトライ対象のレスポンスは呼び出し元へ返す前に body をバッファへ読み切り、接続をすぐプールへ返す。
/// </summary>
public sealed class RetryableResponseBufferingHandler : DelegatingHandler
{
    // エラーレスポンスの body として想定する上限。超えた場合はレスポンスを破棄して例外にする
    private const long MaxBufferSize = 1024 * 1024;

    private readonly ILogger _logger;

    public RetryableResponseBufferingHandler(HttpMessageHandler innerHandler, ILogger logger) : base(innerHandler)
    {
        _logger = logger;
    }

    // ApiClient.RunRequest がリトライするステータスと揃える
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => (int)statusCode is 429 or 500 or 502 or 503 or 504 or 522;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!IsRetryableStatus(response.StatusCode))
        {
            return response;
        }

        // 500/503 以外は ApiClient 側で何もログに出ないので、何が返ってきているか追えるようにしておく
        _logger.LogInformation(
            "{Method} request to {Uri} returned {StatusCode}",
            request.Method,
            request.RequestUri?.GetLeftPart(UriPartial.Path),
            (int)response.StatusCode);
        try
        {
            await response.Content.LoadIntoBufferAsync(MaxBufferSize, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }
        return response;
    }
}
