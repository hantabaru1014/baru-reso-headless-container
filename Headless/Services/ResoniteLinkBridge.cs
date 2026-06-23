using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FrooxEngine;
using Headless.Rpc;
using WatsonWebsocket;

namespace Headless.Services;

/// <summary>
/// per-World に 1 つ存在し、gRPC bidi stream を「仮想 WatsonWebsocket クライアント」として
/// ResoniteLinkService に流し込むブリッジ。WatsonWsServer は起動しない。
/// </summary>
public sealed class ResoniteLinkBridge : IDisposable
{
    private readonly ILogger _logger;
    private readonly ResoniteLinkHost _host;
    private readonly ConcurrentDictionary<Guid, BridgeClient> _clients = new();
    private readonly object _lifecycleLock = new();
    private int _nextSessionId;
    private bool _disposed;

    public ResoniteLinkBridge(World world, ILogger logger)
    {
        _logger = logger;
        _host = new ResoniteLinkHost(world);

        // Host.Start() を呼ばずに outgoing 経路だけ独自にセットアップする。
        // 既存の _messageSender (private) は EnginePrePatcher で public 化済み。
        _host._messageSender = new ActionBlock<ResoniteLinkHost.OutgoingMessage>(DispatchOutgoing);
    }

    public int ClientsCount => _clients.Count;

    /// <summary>
    /// gRPC stream 1 本に対応する仮想クライアントを開く。
    /// </summary>
    public BridgeClient OpenClient()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var metadata = new ClientMetadata
            {
                Guid = Guid.NewGuid(),
                Ip = "127.0.0.1",
                Port = 0,
                Name = "grpc",
            };
            var uniqueSessionId = Interlocked.Increment(ref _nextSessionId).ToString();
            var service = new ResoniteLinkService(_host, uniqueSessionId, metadata);
            var channel = Channel.CreateUnbounded<ResoniteLinkStreamResponse>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            var client = new BridgeClient(metadata, service, channel);
            if (!_clients.TryAdd(metadata.Guid, client))
            {
                throw new InvalidOperationException("Failed to register bridge client (duplicate guid)");
            }
            return client;
        }
    }

    /// <summary>
    /// gRPC からの 1 メッセージを ResoniteLinkService に流し込む。
    /// </summary>
    public void Dispatch(BridgeClient client, ReadOnlyMemory<byte> data, WebSocketMessageType type)
    {
        if (_disposed) return;
        var segment = new ArraySegment<byte>(data.ToArray());
        var args = new MessageReceivedEventArgs(client.Metadata, segment, type);
        client.Service.OnMessage(args);
    }

    /// <summary>
    /// gRPC stream が切断されたときに呼ぶ。
    /// </summary>
    public void CloseClient(BridgeClient client)
    {
        if (!_clients.TryRemove(client.Metadata.Guid, out _))
        {
            return;
        }
        try
        {
            client.Service.OnClose(new DisconnectionEventArgs(client.Metadata));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ResoniteLinkService.OnClose threw");
        }
        client.Outgoing.Writer.TryComplete();
    }

    private void DispatchOutgoing(ResoniteLinkHost.OutgoingMessage message)
    {
        if (!_clients.TryGetValue(message.client.Guid, out var client))
        {
            // クライアントが既に切断済み — 無視
            return;
        }
        var grpcMessage = new ResoniteLinkStreamResponse { TextFrame = message.json };
        if (!client.Outgoing.Writer.TryWrite(grpcMessage))
        {
            _logger.LogWarning("Dropping ResoniteLink outgoing frame: channel closed for {Guid}", message.client.Guid);
        }
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        foreach (var client in _clients.Values)
        {
            client.Outgoing.Writer.TryComplete();
        }
        _clients.Clear();
        _host._messageSender.Complete();
    }

    public sealed class BridgeClient
    {
        public BridgeClient(ClientMetadata metadata, ResoniteLinkService service, Channel<ResoniteLinkStreamResponse> outgoing)
        {
            Metadata = metadata;
            Service = service;
            Outgoing = outgoing;
        }

        public ClientMetadata Metadata { get; }
        public ResoniteLinkService Service { get; }
        public Channel<ResoniteLinkStreamResponse> Outgoing { get; }
    }
}
