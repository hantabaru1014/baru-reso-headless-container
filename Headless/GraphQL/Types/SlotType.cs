using FrooxEngine;
using Headless.GraphQL.Types.Scalars;
using HotChocolate;

namespace Headless.GraphQL.Types;

[ObjectType]
public class SlotType
{
    private readonly Slot _slot;

    public SlotType(Slot slot)
    {
        _slot = slot;
    }

    public string RefId => _slot.ReferenceID.ToString();
    public string Name => _slot.Name ?? "";
    public bool Active => _slot.ActiveSelf;
    public bool Persistent => _slot.IsPersistent;
    public string? Tag => _slot.Tag;

    // Transform - Local
    public Float3Type Position => new Float3Type(_slot.LocalPosition);
    public FloatQType Rotation => new FloatQType(_slot.LocalRotation);
    public Float3Type Scale => new Float3Type(_slot.LocalScale);

    // Transform - Global
    public Float3Type GlobalPosition => new Float3Type(_slot.GlobalPosition);
    public FloatQType GlobalRotation => new FloatQType(_slot.GlobalRotation);
    public Float3Type GlobalScale => new Float3Type(_slot.GlobalScale);

    // Hierarchy
    public SlotType? Parent => _slot.Parent != null ? new SlotType(_slot.Parent) : null;
    public IEnumerable<SlotType> Children => _slot.Children.Select(c => new SlotType(c));
    public int ChildCount => _slot.ChildrenCount;

    // Components
    [GraphQLName("components")]
    public IEnumerable<ComponentType> AllComponents => _slot.Components.Select(c => new ComponentType(c));
    public int ComponentCount => _slot.ComponentCount;

    // SyncMembers
    [GraphQLName("syncMembers")]
    public IEnumerable<ISyncMemberType> AllSyncMembers
    {
        get
        {
            for (int i = 0; i < _slot.SyncMemberCount; i++)
            {
                var member = _slot.GetSyncMember(i);
                var name = _slot.GetSyncMemberName(i);
                yield return SyncMemberTypeFactory.Create(member, name, i);
            }
        }
    }

    [GraphQLName("syncMemberByName")]
    public ISyncMemberType? GetSyncMember(string name)
    {
        for (int i = 0; i < _slot.SyncMemberCount; i++)
        {
            var memberName = _slot.GetSyncMemberName(i);
            if (memberName == name)
            {
                return SyncMemberTypeFactory.Create(_slot.GetSyncMember(i), memberName, i);
            }
        }
        return null;
    }

    [GraphQLName("componentByType")]
    public ComponentType? GetComponent(string typeName)
    {
        var component = _slot.Components.FirstOrDefault(c =>
            c.GetType().Name == typeName || c.GetType().FullName == typeName);
        return component != null ? new ComponentType(component) : null;
    }

    [GraphQLName("componentsByType")]
    public IEnumerable<ComponentType> GetComponents(string typeName)
    {
        return _slot.Components
            .Where(c => c.GetType().Name == typeName || c.GetType().FullName == typeName)
            .Select(c => new ComponentType(c));
    }
}
