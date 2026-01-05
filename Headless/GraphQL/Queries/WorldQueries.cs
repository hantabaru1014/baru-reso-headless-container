using Elements.Core;
using FrooxEngine;
using Headless.GraphQL.Services;
using Headless.GraphQL.Types;

namespace Headless.GraphQL.Queries;

[ExtendObjectType(OperationTypeNames.Query)]
public class WorldQueries
{
    public IEnumerable<WorldType> GetWorlds([Service] FrooxEngineGraphQLService service)
    {
        return service.ListSessions().Select(s => new WorldType(s.Instance));
    }

    public WorldType? GetWorld(
        string sessionId,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        return session != null ? new WorldType(session.Instance) : null;
    }

    public async Task<SlotType?> GetSlot(
        string sessionId,
        string refId,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(refId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            return slot != null ? new SlotType(slot) : null;
        });
    }

    public async Task<ComponentType?> GetComponent(
        string sessionId,
        string refId,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(refId);
            var component = service.FindComponentByRefId(session.Instance, parsedRefId);
            return component != null ? new ComponentType(component) : null;
        });
    }

    public IEnumerable<ComponentTypeInfo> GetComponentTypes(
        string? filter,
        [Service] FrooxEngineGraphQLService service)
    {
        IEnumerable<Type> types = service.GetAllComponentTypes();

        if (!string.IsNullOrEmpty(filter))
        {
            var lowerFilter = filter.ToLowerInvariant();
            types = types.Where(t =>
                (t.Name?.ToLowerInvariant().Contains(lowerFilter) ?? false) ||
                (t.FullName?.ToLowerInvariant().Contains(lowerFilter) ?? false));
        }

        return types.Select(t => new ComponentTypeInfo(t));
    }
}

[ObjectType]
public class ComponentTypeInfo
{
    private readonly Type _type;

    public ComponentTypeInfo(Type type)
    {
        _type = type;
    }

    public string Name => _type.Name;
    public string FullName => _type.FullName ?? _type.Name;
    public string? Category => GetCategory();
    public bool IsGeneric => _type.IsGenericType;
    public string? GenericDefinition => _type.IsGenericType ? _type.GetGenericTypeDefinition().Name : null;

    private string? GetCategory()
    {
        var ns = _type.Namespace;
        if (ns == null) return null;

        if (ns.StartsWith("FrooxEngine.ProtoFlux")) return "ProtoFlux";
        if (ns.StartsWith("FrooxEngine.UIX")) return "UIX";
        if (ns.StartsWith("FrooxEngine.FinalIK")) return "IK";
        if (ns.StartsWith("FrooxEngine.CommonAvatar")) return "Avatar";
        if (ns.StartsWith("FrooxEngine.LogiX")) return "LogiX";
        if (ns.StartsWith("FrooxEngine")) return "Core";
        return ns;
    }
}
