namespace Comuki.Modules.Integrations.Domain.Ids;

/// <summary>
/// Strong-typed identifier of an admission rule — the per-project
/// watch/inbox filter configuration.
/// </summary>
public readonly record struct AdmissionRuleId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// 
    public static AdmissionRuleId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
