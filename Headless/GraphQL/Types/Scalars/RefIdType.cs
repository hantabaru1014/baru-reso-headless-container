using Elements.Core;
using HotChocolate.Language;

namespace Headless.GraphQL.Types.Scalars;

public class RefIdType : ScalarType<RefID, StringValueNode>
{
    public RefIdType() : base("RefID")
    {
        Description = "Represents a FrooxEngine RefID";
    }

    public override IValueNode ParseResult(object? resultValue)
    {
        return ParseValue(resultValue);
    }

    protected override RefID ParseLiteral(StringValueNode valueSyntax)
    {
        return RefID.Parse(valueSyntax.Value);
    }

    protected override StringValueNode ParseValue(RefID runtimeValue)
    {
        return new StringValueNode(runtimeValue.ToString());
    }

    public override bool TrySerialize(object? runtimeValue, out object? resultValue)
    {
        if (runtimeValue is RefID refId)
        {
            resultValue = refId.ToString();
            return true;
        }

        resultValue = null;
        return false;
    }

    public override bool TryDeserialize(object? resultValue, out object? runtimeValue)
    {
        if (resultValue is string str)
        {
            runtimeValue = RefID.Parse(str);
            return true;
        }

        runtimeValue = null;
        return false;
    }
}
