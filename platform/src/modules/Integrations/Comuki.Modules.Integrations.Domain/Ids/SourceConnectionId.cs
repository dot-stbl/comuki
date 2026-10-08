namespace Comuki.Modules.Integrations.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a source connection — the binding of one
/// external tracker (repo / project / queue) to one Comuki project.
/// </summary>
public readonly record struct SourceConnectionId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// 
    public static SourceConnectionId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
