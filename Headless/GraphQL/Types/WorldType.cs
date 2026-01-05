using Elements.Core;
using FrooxEngine;
using Headless.GraphQL.Services;

namespace Headless.GraphQL.Types;

[ObjectType]
public class WorldType
{
    private readonly World _world;

    public WorldType(World world)
    {
        _world = world;
    }

    public string SessionId => _world.SessionId;
    public string Name => _world.Name ?? "";
    public string? Description => _world.Description;
    public int UserCount => _world.UserCount;
    public int MaxUsers => _world.MaxUsers;
    public string AccessLevel => _world.AccessLevel.ToString();

    public SlotType RootSlot => new SlotType(_world.RootSlot);

    public IEnumerable<WorldUserType> Users =>
        _world.AllUsers.Select(u => new WorldUserType(u));

    public WorldUserType? HostUser => _world.HostUser != null
        ? new WorldUserType(_world.HostUser)
        : null;

    public SlotType? FindSlotByRefId(
        string refId,
        [Service] FrooxEngineGraphQLService service)
    {
        if (!service.TryParseRefId(refId, out var parsedRefId)) return null;

        var slot = service.FindSlotByRefId(_world, parsedRefId);
        return slot != null ? new SlotType(slot) : null;
    }

    public IEnumerable<SlotType> FindSlotByName(string name, bool searchChildren = true)
    {
        var slots = new List<Slot>();
        if (searchChildren)
        {
            FindSlotsRecursive(_world.RootSlot, name, slots);
        }
        else
        {
            foreach (var child in _world.RootSlot.Children)
            {
                if (child.Name == name)
                {
                    slots.Add(child);
                }
            }
        }
        return slots.Select(s => new SlotType(s));
    }

    public SlotType? FindSlotByPath(string path)
    {
        var slot = _world.RootSlot.FindChild(s => s.Name == path);
        return slot != null ? new SlotType(slot) : null;
    }

    private void FindSlotsRecursive(Slot parent, string name, List<Slot> results)
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name)
            {
                results.Add(child);
            }
            FindSlotsRecursive(child, name, results);
        }
    }
}

[ObjectType]
public class WorldUserType
{
    private readonly User _user;

    public WorldUserType(User user)
    {
        _user = user;
    }

    public string UserId => _user.UserID ?? "";
    public string UserName => _user.UserName ?? "";
    public string Role => _user.Role?.RoleName?.Value ?? "";
    public bool IsPresent => _user.IsPresent;
    public bool IsHost => _user.IsHost;

    public SlotType? UserRootSlot
    {
        get
        {
            var rootSlot = _user.Root?.Slot;
            return rootSlot != null ? new SlotType(rootSlot) : null;
        }
    }
}
