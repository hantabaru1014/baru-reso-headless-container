using Elements.Core;
using FrooxEngine;
using Headless.Events;
using Headless.Rpc;

namespace Headless.Models;

/// <summary>
/// Tiny <see cref="IWorldEventReceiver"/> shim whose only purpose is to
/// receive <see cref="World.OnWorldSaved"/> callbacks and forward them to
/// the <see cref="HostEventBus"/> as <see cref="WorldSaved"/> events.
///
/// Registering on the world catches every save path that ends up calling
/// <c>World.SaveWorld()</c> — including in-world UI saves, engine-driven
/// autosaves, and anything else that goes through <c>Userspace.SaveWorld</c>.
/// Emitting only from the headless's own <c>SaveWorld</c> wrappers would
/// silently miss saves initiated from inside the running session.
/// </summary>
internal sealed class WorldSavedHook : IWorldEventReceiver, IDisposable
{
    private readonly World _world;
    private readonly HostEventBus _eventBus;
    private int _disposed;

    public WorldSavedHook(World world, HostEventBus eventBus)
    {
        _world = world;
        _eventBus = eventBus;
        _world.RegisterEventReceiver(this);
    }

    // IWorldEventReceiver — only OnWorldSaved is wired; HasEventHandler
    // filters us into that one list at registration time so the other
    // On* methods will never be called.
    public bool HasEventHandler(World.WorldEvent worldEvent) => worldEvent == World.WorldEvent.OnWorldSaved;

    public void OnFocusChanged(World.WorldFocus focus) { }
    public void OnWorldDestroy() { }
    public void OnUserJoined(FrooxEngine.User user) { }
    public void OnUserLeft(FrooxEngine.User user) { }
    public void OnUserSpawn(FrooxEngine.User user) { }

    public void OnWorldSaved()
    {
        // Runs on the engine update thread; do not let an exception
        // propagate back into FrooxEngine's receiver loop.
        try
        {
            var worldUrl = _world.RecordURL?.ToString();
            if (string.IsNullOrEmpty(worldUrl))
            {
                // 観測性のため: 通常 save 完了直後に RecordURL が null になるケースは
                // 想定外。controller の URL 更新が空文字で skip されたときの
                // 根本原因を辿れるように 1 行残しておく
                UniLog.Warning($"WorldSavedHook: RecordURL is null after save for session {_world.SessionId}");
            }
            _eventBus.Emit(new WorldSaved
            {
                SessionId = _world.SessionId,
                WorldUrl = worldUrl ?? "",
            });
        }
        catch (Exception ex)
        {
            UniLog.Warning($"HostEventBus.Emit(WorldSaved) threw: {ex}");
        }
    }

    // IWorldElement — minimal stubs. World expects our IsRemoved to flip
    // true when we no longer want to be dispatched.
    public RefID ReferenceID => default;
    public string Name => nameof(WorldSavedHook);
    public World World => _world;
    public IWorldElement Parent => null!;
    public bool IsLocalElement => true;
    public bool IsPersistent => false;
    public bool IsRemoved => _disposed != 0;
    public void ChildChanged(IWorldElement child) { }
    public DataTreeNode Save(SaveControl control) => null!;
    public void Load(DataTreeNode node, LoadControl control) { }
    public string GetSyncMemberName(ISyncMember member) => string.Empty;

    // IChangeable — required by the interface chain; we never raise it.
#pragma warning disable CS0067 // event never used
    public event Action<IChangeable>? Changed;
#pragma warning restore CS0067

    // IUpdatable — Update / Startup / Destruction hooks are not exercised
    // for an event-receiver-only implementation.
    public bool IsStarted => true;
    public bool IsChangeDirty => false;
    public int LastChangeUpdateIndex => 0;
    public int UpdateOrder => 0;
    public void InternalRunStartup() { }
    public void InternalRunUpdate() { }
    public void InternalRunApplyChanges(int changeUpdateIndex) { }
    public void InternalRunDestruction() { }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _world.UnregisterEventReceiver(this);
    }
}
