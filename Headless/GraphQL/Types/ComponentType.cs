using FrooxEngine;
using HotChocolate;

namespace Headless.GraphQL.Types;

[ObjectType]
public class ComponentType
{
    private readonly Component _component;

    public ComponentType(Component component)
    {
        _component = component;
    }

    public string RefId => _component.ReferenceID.ToString();
    public string TypeName => _component.GetType().Name;
    public string TypeFullName => _component.GetType().FullName ?? _component.GetType().Name;
    public bool Enabled => _component.Enabled;
    public bool IsPersistent => _component.IsPersistent;

    public SlotType Slot => new SlotType(_component.Slot);

    public int SyncMemberCount => _component.SyncMemberCount;

    [GraphQLName("syncMembers")]
    public IEnumerable<ISyncMemberType> AllSyncMembers
    {
        get
        {
            for (int i = 0; i < _component.SyncMemberCount; i++)
            {
                var member = _component.GetSyncMember(i);
                var name = _component.GetSyncMemberName(i);
                yield return SyncMemberTypeFactory.Create(member, name, i);
            }
        }
    }

    [GraphQLName("syncMemberByName")]
    public ISyncMemberType? GetSyncMember(string name)
    {
        for (int i = 0; i < _component.SyncMemberCount; i++)
        {
            var memberName = _component.GetSyncMemberName(i);
            if (memberName == name)
            {
                return SyncMemberTypeFactory.Create(_component.GetSyncMember(i), memberName, i);
            }
        }
        return null;
    }

    [GraphQLName("syncMemberByIndex")]
    public ISyncMemberType? GetSyncMemberByIndex(int index)
    {
        if (index < 0 || index >= _component.SyncMemberCount)
        {
            return null;
        }
        var member = _component.GetSyncMember(index);
        var name = _component.GetSyncMemberName(index);
        return SyncMemberTypeFactory.Create(member, name, index);
    }
}
