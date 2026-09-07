namespace Comuki.Modules.Projects.Domain.DomainTypes;

/// <summary>
/// Strong-typed identifier of a domain-type admission policy — the
/// per-project answer to "may work of domain type X enter from source Y".
/// </summary>
/// <param name="Value"></param>
public readonly record struct DomainTypeAdmissionId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns></returns>
    public static DomainTypeAdmissionId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
