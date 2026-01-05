using Elements.Core;
using FrooxEngine;
using Headless.GraphQL.Services;
using Headless.GraphQL.Types;
using IField = FrooxEngine.IField;

namespace Headless.GraphQL.Mutations;

[ExtendObjectType(OperationTypeNames.Mutation)]
public class SyncMemberMutations
{
    public async Task<SetSyncFieldResult> SetSyncFieldValue(
        string sessionId,
        string componentRefId,
        string memberName,
        string value,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null)
        {
            return new SetSyncFieldResult(false, null, null, "Session not found");
        }

        if (!service.TryParseRefId(componentRefId, out var parsedRefId))
        {
            return new SetSyncFieldResult(false, null, null, "Invalid RefID format");
        }

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var component = service.FindComponentByRefId(session.Instance, parsedRefId);
            if (component == null)
            {
                return new SetSyncFieldResult(false, null, null, "Component not found");
            }

            var member = component.GetSyncMember(memberName);
            if (member == null)
            {
                return new SetSyncFieldResult(false, null, null, $"SyncMember '{memberName}' not found");
            }

            if (member is not IField field)
            {
                return new SetSyncFieldResult(false, null, null, $"SyncMember '{memberName}' is not a field");
            }

            var previousValue = field.BoxedValue?.ToString();

            if (service.SetSyncFieldValue(field, value))
            {
                var newValue = field.BoxedValue?.ToString();
                return new SetSyncFieldResult(true, previousValue, newValue, null);
            }

            return new SetSyncFieldResult(false, previousValue, null, "Failed to set value");
        });
    }

    public async Task<SetSyncRefResult> SetSyncRefTarget(
        string sessionId,
        string componentRefId,
        string memberName,
        string? targetRefId,
        [Service] FrooxEngineGraphQLService service)
    {
        var session = service.GetSession(sessionId);
        if (session == null)
        {
            return new SetSyncRefResult(false, null, null, "Session not found");
        }

        if (!service.TryParseRefId(componentRefId, out var parsedRefId))
        {
            return new SetSyncRefResult(false, null, null, "Invalid RefID format");
        }

        RefID? parsedTargetRefId = null;
        if (!string.IsNullOrEmpty(targetRefId))
        {
            if (!service.TryParseRefId(targetRefId, out var targetParsed))
            {
                return new SetSyncRefResult(false, null, null, "Invalid target RefID format");
            }
            parsedTargetRefId = targetParsed;
        }

        return await service.ExecuteOnWorldThread(session.Instance, () =>
        {
            var component = service.FindComponentByRefId(session.Instance, parsedRefId);
            if (component == null)
            {
                return new SetSyncRefResult(false, null, null, "Component not found");
            }

            var member = component.GetSyncMember(memberName);
            if (member == null)
            {
                return new SetSyncRefResult(false, null, null, $"SyncMember '{memberName}' not found");
            }

            if (member is not ISyncRef syncRef)
            {
                return new SetSyncRefResult(false, null, null, $"SyncMember '{memberName}' is not a SyncRef");
            }

            var valueProperty = syncRef.GetType().GetProperty("Value");
            var previousRefId = valueProperty?.GetValue(syncRef) as RefID?;
            var previousRefIdStr = previousRefId?.ToString();

            if (service.SetSyncRefTarget(syncRef, parsedTargetRefId, session.Instance))
            {
                return new SetSyncRefResult(true, previousRefIdStr, targetRefId, null);
            }

            return new SetSyncRefResult(false, previousRefIdStr, null, "Failed to set target");
        });
    }
}

[ObjectType]
public record SetSyncFieldResult(
    bool Success,
    string? PreviousValue,
    string? NewValue,
    string? Error
);

[ObjectType]
public record SetSyncRefResult(
    bool Success,
    string? PreviousRefId,
    string? NewRefId,
    string? Error
);
