using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Headless.Rpc;
using Headless.Tests.Fixtures;
using Headless.Tests.Helpers;

namespace Headless.Tests.IntegrationTests;

/// <summary>
/// Tests for the ResoniteLinkStream bidi RPC and the Session.resonite_link_clients_count field.
/// </summary>
[Collection("Container")]
public class ResoniteLinkStreamTests
{
    private readonly ContainerFixture _fixture;

    public ResoniteLinkStreamTests(ContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ResoniteLinkStream_RejectsNonExistentSession()
    {
        await EnsureReadyAsync();
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        using var call = client.ResoniteLinkStream();
        await call.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
        {
            Init = new ResoniteLinkInit { SessionId = "S-U-does-not-exist:nope" }
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync())
            {
                // 何も読まれない想定
            }
        });
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task ResoniteLinkStream_RejectsMissingInit()
    {
        await EnsureReadyAsync();
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        using var call = client.ResoniteLinkStream();
        // Init を送らずにいきなり text_frame を送る
        await call.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
        {
            TextFrame = "{}"
        });
        await call.RequestStream.CompleteAsync();

        var ex = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var _ in call.ResponseStream.ReadAllAsync())
            {
            }
        });
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task ResoniteLinkStream_OpenAndClose_UpdatesClientsCount()
    {
        await EnsureReadyAsync();
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            // Pre-condition: 0
            var before = await client.GetSessionAsync(new GetSessionRequest { SessionId = sessionId });
            Assert.Equal(0, before.Session.ResoniteLinkClientsCount);

            using var call = client.ResoniteLinkStream();
            await call.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
            {
                Init = new ResoniteLinkInit { SessionId = sessionId }
            });

            // Ready 受信
            Assert.True(await call.ResponseStream.MoveNext(default), "Expected Ready frame");
            var ready = call.ResponseStream.Current;
            Assert.Equal(ResoniteLinkStreamResponse.PayloadOneofCase.Ready, ready.PayloadCase);

            // クライアント数が増えるのを少し待つ (Bridge への登録は同期だが、proto 取得側で取りこぼしを避ける)
            await WaitForCountAsync(client, sessionId, 1, TimeSpan.FromSeconds(5));

            // RequestSessionData を送ると text_frame で応答が返る (応答 JSON の内容は問わない)
            var requestJson = JsonSerializer.Serialize(new
            {
                _type_ = "requestSessionData",
                messageId = "m1",
            });
            // System.Text.Json は $type をエスケープしてしまうので手動で組み立てる
            requestJson = "{\"$type\":\"requestSessionData\",\"messageId\":\"m1\"}";
            await call.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
            {
                TextFrame = requestJson
            });

            var responded = await WaitForTextFrameAsync(call.ResponseStream, TimeSpan.FromSeconds(10));
            Assert.NotNull(responded);
            Assert.False(string.IsNullOrEmpty(responded));

            // クライアント側から終了
            await call.RequestStream.CompleteAsync();
            // サーバ側 stream を消費しきる
            await DrainAsync(call.ResponseStream);

            // クライアント数が 0 に戻る
            await WaitForCountAsync(client, sessionId, 0, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    [Fact]
    public async Task ResoniteLinkStream_TwoConcurrentClients_ReflectedInCount()
    {
        await EnsureReadyAsync();
        using var channel = GrpcChannel.ForAddress(_fixture.GrpcEndpoint);
        var client = new HeadlessControlService.HeadlessControlServiceClient(channel);

        var sessionId = await StartGridSessionAsync(client);
        try
        {
            using var callA = client.ResoniteLinkStream();
            await callA.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
            {
                Init = new ResoniteLinkInit { SessionId = sessionId }
            });
            Assert.True(await callA.ResponseStream.MoveNext(default));
            await WaitForCountAsync(client, sessionId, 1, TimeSpan.FromSeconds(5));

            using var callB = client.ResoniteLinkStream();
            await callB.RequestStream.WriteAsync(new ResoniteLinkStreamRequest
            {
                Init = new ResoniteLinkInit { SessionId = sessionId }
            });
            Assert.True(await callB.ResponseStream.MoveNext(default));
            await WaitForCountAsync(client, sessionId, 2, TimeSpan.FromSeconds(5));

            // A だけ閉じる
            await callA.RequestStream.CompleteAsync();
            await DrainAsync(callA.ResponseStream);
            await WaitForCountAsync(client, sessionId, 1, TimeSpan.FromSeconds(5));

            // B も閉じる
            await callB.RequestStream.CompleteAsync();
            await DrainAsync(callB.ResponseStream);
            await WaitForCountAsync(client, sessionId, 0, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await client.StopSessionAsync(new StopSessionRequest { SessionId = sessionId });
        }
    }

    private async Task EnsureReadyAsync()
    {
        var ready = await LogPollingHelper.WaitForApplicationStartupAsync(
            _fixture.GetLogsAsync,
            TimeSpan.FromMinutes(5));
        Assert.True(ready, "Application startup did not complete in time");
    }

    private static async Task<string> StartGridSessionAsync(HeadlessControlService.HeadlessControlServiceClient client)
    {
        var startResponse = await client.StartWorldAsync(new StartWorldRequest
        {
            Parameters = new WorldStartupParameters
            {
                LoadWorldPresetName = "Grid",
                AccessLevel = AccessLevel.Private,
            }
        });
        Assert.NotNull(startResponse.OpenedSession);
        Assert.False(string.IsNullOrEmpty(startResponse.OpenedSession.Id));
        // セッションの初期化が落ち着くのを少し待つ
        await Task.Delay(TimeSpan.FromSeconds(5));
        return startResponse.OpenedSession.Id;
    }

    private static async Task WaitForCountAsync(
        HeadlessControlService.HeadlessControlServiceClient client,
        string sessionId,
        int expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        int last = -1;
        while (DateTime.UtcNow < deadline)
        {
            var resp = await client.GetSessionAsync(new GetSessionRequest { SessionId = sessionId });
            last = resp.Session.ResoniteLinkClientsCount;
            if (last == expected) return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        Assert.Fail($"resonite_link_clients_count did not reach {expected} within {timeout} (last={last})");
    }

    private static async Task<string?> WaitForTextFrameAsync(
        IAsyncStreamReader<ResoniteLinkStreamResponse> reader,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (await reader.MoveNext(cts.Token))
            {
                if (reader.Current.PayloadCase == ResoniteLinkStreamResponse.PayloadOneofCase.TextFrame)
                {
                    return reader.Current.TextFrame;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
        }
        return null;
    }

    private static async Task DrainAsync(IAsyncStreamReader<ResoniteLinkStreamResponse> reader)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (await reader.MoveNext(cts.Token))
            {
                // discard
            }
        }
        catch
        {
            // 正常終了 / cancel
        }
    }
}
