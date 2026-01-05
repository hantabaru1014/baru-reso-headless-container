using Elements.Core;
using HotChocolate.Types;

namespace Headless.GraphQL.Types.Scalars;

[ObjectType]
public class Float3Type
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }

    public Float3Type(float3 value)
    {
        X = value.x;
        Y = value.y;
        Z = value.z;
    }

    public Float3Type(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [GraphQLIgnore]
    public float3 ToFloat3() => new float3(X, Y, Z);
}

[ObjectType]
public class FloatQType
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }

    public FloatQType(floatQ value)
    {
        X = value.x;
        Y = value.y;
        Z = value.z;
        W = value.w;
    }

    [GraphQLIgnore]
    public floatQ ToFloatQ() => new floatQ(X, Y, Z, W);
}

[InputObjectType]
public class Float3Input
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    [GraphQLIgnore]
    public float3 ToFloat3() => new float3(X, Y, Z);
}

[InputObjectType]
public class FloatQInput
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    [GraphQLIgnore]
    public floatQ ToFloatQ() => new floatQ(X, Y, Z, W);
}
