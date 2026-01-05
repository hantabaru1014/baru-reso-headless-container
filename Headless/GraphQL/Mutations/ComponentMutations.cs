using Elements.Core;
using FrooxEngine;
using Headless.GraphQL.Services;
using Headless.GraphQL.Types;

namespace Headless.GraphQL.Mutations;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class ComponentMutations
{
    public async Task<ComponentType?> AttachComponent(
        string sessionId,
        string slotRefId,
        string componentTypeName,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        var componentType = service.FindComponentType(componentTypeName);
        if (componentType == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            var component = slot.AttachComponent(componentType);
            return component != null ? new ComponentType(component) : null;
        });
    }

    public async Task<RemoveComponentResult> RemoveComponent(
        string sessionId,
        string componentRefId,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null)
        {
            return new RemoveComponentResult(false, componentRefId, null, "Session not found");
        }

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(componentRefId);
            var component = service.FindComponentByRefId(session.Instance, parsedRefId);
            if (component == null)
            {
                return new RemoveComponentResult(false, componentRefId, null, "Component not found");
            }

            var typeName = component.GetType().FullName ?? component.GetType().Name;
            component.Destroy();

            return new RemoveComponentResult(true, componentRefId, typeName, null);
        });
    }

    public async Task<ComponentType?> SetComponentEnabled(
        string sessionId,
        string componentRefId,
        bool enabled,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(componentRefId);
            var component = service.FindComponentByRefId(session.Instance, parsedRefId);
            if (component == null) return null;

            component.Enabled = enabled;
            return new ComponentType(component);
        });
    }
}

[ObjectType]
public record RemoveComponentResult(
    bool Success,
    string RemovedRefId,
    string? RemovedTypeName,
    string? Error
);
