using System.Text.Json;
using Elements.Core;
using FrooxEngine;
using IField = FrooxEngine.IField;

namespace Headless.GraphQL.Types;

[InterfaceType]
public interface ISyncMemberType
{
    string Name { get; }
    int Index { get; }
    string TypeName { get; }
    bool IsLinked { get; }
}

[ObjectType]
public class SyncFieldType : ISyncMemberType
{
    private readonly IField _field;
    private readonly string _name;
    private readonly int _index;

    public SyncFieldType(IField field, string name, int index)
    {
        _field = field;
        _name = name;
        _index = index;
    }

    public string Name => _name;
    public int Index => _index;
    public string TypeName => _field.GetType().Name;
    public bool IsLinked => ((ISyncMember)_field).IsLinked;

    public string ValueType => _field.ValueType.Name;
    public string ValueTypeFullName => _field.ValueType.FullName ?? _field.ValueType.Name;

    public string? Value
    {
        get
        {
            try
            {
                var boxed = _field.BoxedValue;
                if (boxed == null) return null;

                // Handle primitive types directly
                if (boxed is string s) return s;
                if (boxed is bool b) return b.ToString().ToLowerInvariant();
                if (boxed is int or long or float or double or decimal)
                {
                    return boxed.ToString();
                }
                if (boxed is RefID refId)
                {
                    return refId.ToString();
                }

                // For complex types, serialize to JSON
                return JsonSerializer.Serialize(boxed);
            }
            catch
            {
                return null;
            }
        }
    }

    public string? ValueAsString => _field.BoxedValue?.ToString();

    public float? ValueAsFloat
    {
        get
        {
            var boxed = _field.BoxedValue;
            if (boxed is float f) return f;
            if (boxed is double d) return (float)d;
            if (boxed is int i) return i;
            if (boxed is long l) return l;
            return null;
        }
    }

    public int? ValueAsInt
    {
        get
        {
            var boxed = _field.BoxedValue;
            if (boxed is int i) return i;
            if (boxed is long l) return (int)l;
            if (boxed is float f) return (int)f;
            if (boxed is double d) return (int)d;
            return null;
        }
    }

    public bool? ValueAsBool
    {
        get
        {
            if (_field.BoxedValue is bool b) return b;
            return null;
        }
    }
}

[ObjectType]
public class SyncRefType : ISyncMemberType
{
    private readonly ISyncRef _syncRef;
    private readonly string _name;
    private readonly int _index;

    public SyncRefType(ISyncRef syncRef, string name, int index)
    {
        _syncRef = syncRef;
        _name = name;
        _index = index;
    }

    public string Name => _name;
    public int Index => _index;
    public string TypeName => _syncRef.GetType().Name;
    public bool IsLinked => ((ISyncMember)_syncRef).IsLinked;

    public string? TargetTypeName
    {
        get
        {
            var targetType = _syncRef.GetType();
            if (targetType.IsGenericType)
            {
                var genericArgs = targetType.GetGenericArguments();
                if (genericArgs.Length > 0)
                {
                    return genericArgs[0].Name;
                }
            }
            return null;
        }
    }

    public string? TargetRefId
    {
        get
        {
            try
            {
                var valueProperty = _syncRef.GetType().GetProperty("Value");
                if (valueProperty?.GetValue(_syncRef) is RefID refId && refId != default)
                {
                    return refId.ToString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    public SlotType? TargetAsSlot
    {
        get
        {
            try
            {
                var targetProperty = _syncRef.GetType().GetProperty("Target");
                if (targetProperty?.GetValue(_syncRef) is Slot slot)
                {
                    return new SlotType(slot);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    public ComponentType? TargetAsComponent
    {
        get
        {
            try
            {
                var targetProperty = _syncRef.GetType().GetProperty("Target");
                if (targetProperty?.GetValue(_syncRef) is Component component)
                {
                    return new ComponentType(component);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}

[ObjectType]
public class GenericSyncMemberType : ISyncMemberType
{
    private readonly ISyncMember _member;
    private readonly string _name;
    private readonly int _index;

    public GenericSyncMemberType(ISyncMember member, string name, int index)
    {
        _member = member;
        _name = name;
        _index = index;
    }

    public string Name => _name;
    public int Index => _index;
    public string TypeName => _member.GetType().Name;
    public bool IsLinked => _member.IsLinked;
}

public static class SyncMemberTypeFactory
{
    public static ISyncMemberType Create(ISyncMember member, string name, int index)
    {
        return member switch
        {
            IField field => new SyncFieldType(field, name, index),
            ISyncRef syncRef => new SyncRefType(syncRef, name, index),
            _ => new GenericSyncMemberType(member, name, index)
        };
    }
}
