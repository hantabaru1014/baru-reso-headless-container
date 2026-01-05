using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using Headless.Models;
using Headless.Services;
using IField = FrooxEngine.IField;

namespace Headless.GraphQL.Services;

public class FrooxEngineGraphQLService
{
    private readonly WorldService _worldService;
    private readonly ILogger<FrooxEngineGraphQLService> _logger;
    private readonly Lazy<IReadOnlyList<Type>> _componentTypesCache;

    public FrooxEngineGraphQLService(WorldService worldService, ILogger<FrooxEngineGraphQLService> logger)
    {
        _worldService = worldService;
        _logger = logger;
        _componentTypesCache = new Lazy<IReadOnlyList<Type>>(ScanComponentTypes);
    }

    public RunningSession? GetSession(string sessionId) => _worldService.GetSession(sessionId);

    public IEnumerable<RunningSession> ListSessions() => _worldService.ListAll();

    public async Task<T> ExecuteOnWorldThread<T>(World world, Func<T> action)
    {
        return await world.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            return action();
        });
    }

    public async Task ExecuteOnWorldThread(World world, Func<Task> action)
    {
        await world.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            await action();
        });
    }

    public async Task<T> ExecuteOnWorldThreadAsync<T>(World world, Func<Task<T>> action)
    {
        return await world.Coroutines.StartTask(async () =>
        {
            await default(ToWorld);
            return await action();
        });
    }

    public Slot? FindSlotByRefId(World world, RefID refId)
    {
        return world.ReferenceController.GetObjectOrNull(refId) as Slot;
    }

    public Component? FindComponentByRefId(World world, RefID refId)
    {
        return world.ReferenceController.GetObjectOrNull(refId) as Component;
    }

    public IWorldElement? FindElementByRefId(World world, RefID refId)
    {
        return world.ReferenceController.GetObjectOrNull(refId);
    }

    public bool TryParseRefId(string refIdString, out RefID refId)
    {
        try
        {
            refId = RefID.Parse(refIdString);
            return true;
        }
        catch
        {
            refId = default;
            return false;
        }
    }

    public object? GetSyncMemberValue(ISyncMember member)
    {
        if (member is IField field)
        {
            return field.BoxedValue;
        }
        return null;
    }

    public bool SetSyncFieldValue(IField field, string jsonValue)
    {
        try
        {
            var value = DeserializeValue(field.ValueType, jsonValue);
            if (value == null)
            {
                var underlyingType = Nullable.GetUnderlyingType(field.ValueType);
                if (underlyingType == null && field.ValueType.IsValueType)
                {
                    _logger.LogWarning("Cannot set null to non-nullable value type field");
                    return false;
                }
            }
            field.BoxedValue = value!;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set sync field value");
            return false;
        }
    }

    public bool SetSyncRefTarget(ISyncRef syncRef, RefID? targetRefId, World world)
    {
        try
        {
            if (targetRefId == null || targetRefId == default(RefID))
            {
                var valueProperty = syncRef.GetType().GetProperty("Value");
                valueProperty?.SetValue(syncRef, default(RefID));
            }
            else
            {
                var target = world.ReferenceController.GetObjectOrNull(targetRefId.Value);
                if (target == null)
                {
                    _logger.LogWarning("Target not found for RefID: {RefId}", targetRefId);
                    return false;
                }
                var targetProperty = syncRef.GetType().GetProperty("Target");
                targetProperty?.SetValue(syncRef, target);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set sync ref target");
            return false;
        }
    }

    private object? DeserializeValue(Type type, string jsonValue)
    {
        if (type == typeof(string)) return jsonValue;
        if (type == typeof(int)) return int.Parse(jsonValue);
        if (type == typeof(long)) return long.Parse(jsonValue);
        if (type == typeof(float)) return float.Parse(jsonValue);
        if (type == typeof(double)) return double.Parse(jsonValue);
        if (type == typeof(bool)) return bool.Parse(jsonValue);
        if (type == typeof(RefID)) return RefID.Parse(jsonValue);

        return JsonSerializer.Deserialize(jsonValue, type);
    }

    public Type? FindComponentType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type != null && typeof(Component).IsAssignableFrom(type))
        {
            return type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type != null && typeof(Component).IsAssignableFrom(type))
            {
                return type;
            }
        }

        return null;
    }

    public IReadOnlyList<Type> GetAllComponentTypes() => _componentTypesCache.Value;

    private IReadOnlyList<Type> ScanComponentTypes()
    {
        _logger.LogInformation("Scanning component types...");
        var componentType = typeof(Component);
        var types = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                // Only scan FrooxEngine and related assemblies
                var assemblyName = assembly.GetName().Name;
                if (assemblyName == null ||
                    (!assemblyName.StartsWith("FrooxEngine") &&
                     !assemblyName.StartsWith("ProtoFlux") &&
                     !assemblyName.StartsWith("Elements")))
                {
                    continue;
                }

                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsClass && !type.IsAbstract && componentType.IsAssignableFrom(type))
                    {
                        types.Add(type);
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be scanned
            }
        }

        var result = types.OrderBy(t => t.FullName).ToList();
        _logger.LogInformation("Found {Count} component types", result.Count);
        return result;
    }
}
