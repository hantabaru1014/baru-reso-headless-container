using FrooxEngine;
using Grpc.Core;
using Headless.Rpc;
using SkyFrost.Base;

namespace Headless.Services;

public partial class GrpcControllerService
{
    public override async Task ResoniteLinkStream(
        IAsyncStreamReader<ResoniteLinkStreamRequest> requestStream,
        IServerStreamWriter<ResoniteLinkStreamResponse> responseStream,
        ServerCallContext context)
    {
        var ct = context.CancellationToken;
        if (!await requestStream.MoveNext(ct))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Stream closed before init"));
        }
        var first = requestStream.Current;
        if (first.PayloadCase != ResoniteLinkStreamRequest.PayloadOneofCase.Init)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "First message must be ResoniteLinkInit"));
        }
        var sessionId = first.Init.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "session_id is required"));
        }
        var session = _worldService.GetSession(sessionId);
        if (session is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Session not found"));
        }
        if (!session.Instance.IsAllowedToRunResoniteLink())
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "ResoniteLink is not allowed by the permissions in this world"));
        }

        var bridge = session.GetOrCreateLinkBridge(_loggerFactory.CreateLogger<ResoniteLinkBridge>());
        var client = bridge.OpenClient();
        try
        {
            await responseStream.WriteAsync(new ResoniteLinkStreamResponse { Ready = new ResoniteLinkReady() }, ct);

            var writerTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var msg in client.Outgoing.Reader.ReadAllAsync(ct))
                    {
                        await responseStream.WriteAsync(msg, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 通常終了
                }
            }, ct);

            try
            {
                while (await requestStream.MoveNext(ct))
                {
                    var msg = requestStream.Current;
                    switch (msg.PayloadCase)
                    {
                        case ResoniteLinkStreamRequest.PayloadOneofCase.TextFrame:
                            var textBytes = System.Text.Encoding.UTF8.GetBytes(msg.TextFrame);
                            bridge.Dispatch(client, textBytes, System.Net.WebSockets.WebSocketMessageType.Text);
                            break;
                        case ResoniteLinkStreamRequest.PayloadOneofCase.BinaryFrame:
                            bridge.Dispatch(client, msg.BinaryFrame.Memory, System.Net.WebSockets.WebSocketMessageType.Binary);
                            break;
                        case ResoniteLinkStreamRequest.PayloadOneofCase.Init:
                            throw new RpcException(new Status(StatusCode.InvalidArgument, "Init can only be sent once"));
                        default:
                            // 未知の payload は無視
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 通常終了
            }

            // reader 終了に合わせて writer も閉じる
            client.Outgoing.Writer.TryComplete();
            try { await writerTask; } catch (OperationCanceledException) { }
        }
        finally
        {
            bridge.CloseClient(client);
        }
    }
}
