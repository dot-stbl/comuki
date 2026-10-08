namespace Comuki.Modules.Integrations.Domain.Ids;

/// <summary>
/// Strong-typed identifier of an inbound item — one seen external issue
/// inside one project scope.
/// </summary>
public readonly record struct InboundItemId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// 
    public static InboundItemId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
