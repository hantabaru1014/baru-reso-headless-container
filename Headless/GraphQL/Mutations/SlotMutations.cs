using Elements.Core;
using FrooxEngine;
using Headless.GraphQL.Services;
using Headless.GraphQL.Types;
using Headless.GraphQL.Types.Scalars;

namespace Headless.GraphQL.Mutations;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class SlotMutations
{
    public async Task<SlotType?> SetSlotActive(
        string sessionId,
        string slotRefId,
        bool active,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            slot.ActiveSelf = active;
            return new SlotType(slot);
        });
    }

    public async Task<SlotType?> SetSlotName(
        string sessionId,
        string slotRefId,
        string name,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            slot.Name = name;
            return new SlotType(slot);
        });
    }

    public async Task<SlotType?> SetSlotPosition(
        string sessionId,
        string slotRefId,
        float x,
        float y,
        float z,
        bool global = false,
        [Service] FrooxEngineGraphQLService service = null!)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            var position = new float3(x, y, z);
            if (global)
            {
                slot.GlobalPosition = position;
            }
            else
            {
                slot.LocalPosition = position;
            }
            return new SlotType(slot);
        });
    }

    public async Task<SlotType?> SetSlotRotation(
        string sessionId,
        string slotRefId,
        float x,
        float y,
        float z,
        float w,
        bool global = false,
        [Service] FrooxEngineGraphQLService service = null!)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            var rotation = new floatQ(x, y, z, w);
            if (global)
            {
                slot.GlobalRotation = rotation;
            }
            else
            {
                slot.LocalRotation = rotation;
            }
            return new SlotType(slot);
        });
    }

    public async Task<SlotType?> SetSlotScale(
        string sessionId,
        string slotRefId,
        float x,
        float y,
        float z,
        bool global = false,
        [Service] FrooxEngineGraphQLService service = null!)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null) return null;

            var scale = new float3(x, y, z);
            if (global)
            {
                slot.GlobalScale = scale;
            }
            else
            {
                slot.LocalScale = scale;
            }
            return new SlotType(slot);
        });
    }

    public async Task<SlotType?> AddChildSlot(
        string sessionId,
        string parentSlotRefId,
        string name,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null) return null;

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(parentSlotRefId);
            var parentSlot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (parentSlot == null) return null;

            var newSlot = parentSlot.AddSlot(name);
            return new SlotType(newSlot);
        });
    }

    public async Task<DeleteSlotResult> DeleteSlot(
        string sessionId,
        string slotRefId,
        bool preserveChildren = false,
        [Service] FrooxEngineGraphQLService service = null!)
    {
        var session = service.GetSession(sessionId);
        if (session == null)
        {
            return new DeleteSlotResult(false, slotRefId, "Session not found");
        }

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var parsedRefId = RefID.Parse(slotRefId);
            var slot = service.FindSlotByRefId(session.Instance, parsedRefId);
            if (slot == null)
            {
                return new DeleteSlotResult(false, slotRefId, "Slot not found");
            }

            if (slot == session.Instance.RootSlot)
            {
                return new DeleteSlotResult(false, slotRefId, "Cannot delete root slot");
            }

            if (preserveChildren && slot.Parent != null)
            {
                var children = slot.Children.ToList();
                foreach (var child in children)
                {
                    child.SetParent(slot.Parent, false);
                }
            }

            slot.Destroy();
            return new DeleteSlotResult(true, slotRefId, null);
        });
    }
}

[ObjectType]
public record DeleteSlotResult(
    bool Success,
    string DeletedRefId,
    string? Error
);
