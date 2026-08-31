namespace Comuki.Modules.Identity.Domain.Ids;

/// <summary>
/// Strong-typed identifier of an API key row. UUIDv7 like every Identity
/// entity id.
/// </summary>
/// <param name="Value"></param>
public readonly record struct ApiKeyId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static ApiKeyId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
