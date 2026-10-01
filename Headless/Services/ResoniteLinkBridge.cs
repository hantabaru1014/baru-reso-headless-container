using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using Headless.Rpc;
using WatsonWebsocket;

namespace Headless.Services;

/// <summary>
/// per-World に 1 つ存在し、gRPC bidi stream を「仮想 WatsonWebsocket クライアント」として
/// ResoniteLinkService に流し込むブリッジ。WatsonWsServer は起動しない。
/// <para>
/// ResoniteLinkHost は接続ユーザー (Resonite ユーザー ID) ごとに 1 つ持つ。
/// 本家の ResoniteLink はスロットのアクセス可否 (SimpleAvatarProtection) を World.LocalUser 基準で
/// 判定するが、ヘッドレスでは LocalUser がヘッドレスアカウントになるため、他ユーザーのアバターが
/// 一律で触れなくなる。ユーザーごとの Host で判定をそのユーザー基準に差し替えることで、
/// そのユーザーがローカルで ResoniteLink を開始したときと同じ挙動にする。
/// </para>
/// </summary>
public sealed class ResoniteLinkBridge : IDisposable
{
    /// <summary>
    /// ユーザー ID 未指定の接続に使う Host のキー。本家同様 LocalUser 基準で判定する。
    /// </summary>
    private const string DefaultHostKey = "";

    private readonly World _world;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ResoniteLinkHost> _hosts = new();
    private readonly ConcurrentDictionary<Guid, BridgeClient> _clients = new();
    private readonly object _lifecycleLock = new();
    private int _nextSessionId;
    private bool _disposed;

    public ResoniteLinkBridge(World world, ILogger logger)
    {
        _world = world;
        _logger = logger;
    }

    public int ClientsCount => _clients.Count;

    /// <summary>
    /// gRPC stream 1 本に対応する仮想クライアントを開く。
    /// </summary>
    /// <param name="userId">
    /// 接続を発行した Resonite ユーザー ID。null / 空ならヘッドレスアカウント基準の Host を使う。
    /// </param>
    public BridgeClient OpenClient(string? userId = null)
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var hostKey = string.IsNullOrWhiteSpace(userId) ? DefaultHostKey : userId.Trim();
            var host = _hosts.GetOrAdd(hostKey, CreateHost);

            var metadata = new ClientMetadata
            {
                Guid = Guid.NewGuid(),
                Ip = "127.0.0.1",
                Port = 0,
                Name = "grpc",
            };
            var uniqueSessionId = Interlocked.Increment(ref _nextSessionId).ToString();
            var service = new ResoniteLinkService(host, uniqueSessionId, metadata);
            var channel = Channel.CreateUnbounded<ResoniteLinkStreamResponse>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            var client = new BridgeClient(metadata, service, channel, hostKey == DefaultHostKey ? null : hostKey);
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

    private ResoniteLinkHost CreateHost(string hostKey)
    {
        var host = new ResoniteLinkHost(_world);

        // Host.Start() を呼ばずに outgoing 経路だけ独自にセットアップする。
        // 既存の _messageSender (private) は EnginePrePatcher で public 化済み。
        host._messageSender = new ActionBlock<ResoniteLinkHost.OutgoingMessage>(DispatchOutgoing);

        if (hostKey != DefaultHostKey)
        {
            // CanProcessSlotOverride は EnginePrePatcher (AddResoniteLinkSlotAccessHook) で追加したフック。
            host.Translator.CanProcessSlotOverride = slot => CanProcessSlotAs(slot, hostKey);
        }
        return host;
    }

    /// <summary>
    /// ResoniteLinkTranslator.CanProcessSlot と同じ判定を、LocalUser の代わりに
    /// <paramref name="userId"/> を基準に行う (SimpleAvatarProtection.CanUse 相当)。
    /// </summary>
    internal static bool CanProcessSlotAs(Slot slot, string userId)
    {
        var protection = slot.GetComponent<SimpleAvatarProtection>();
        if (protection is null)
        {
            return true;
        }
        var registeredUserId = protection.User.UserId;
        return string.IsNullOrEmpty(registeredUserId) || registeredUserId == userId;
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
        foreach (var host in _hosts.Values)
        {
            host._messageSender.Complete();
        }
        _hosts.Clear();
    }

    public sealed class BridgeClient
    {
        public BridgeClient(ClientMetadata metadata, ResoniteLinkService service, Channel<ResoniteLinkStreamResponse> outgoing, string? userId)
        {
            Metadata = metadata;
            Service = service;
            Outgoing = outgoing;
            UserId = userId;
        }

        public ClientMetadata Metadata { get; }
        public ResoniteLinkService Service { get; }
        public Channel<ResoniteLinkStreamResponse> Outgoing { get; }

        /// <summary>
        /// この接続のアクセス判定基準となる Resonite ユーザー ID。null ならヘッドレスアカウント基準。
        /// </summary>
        public string? UserId { get; }
    }
}
