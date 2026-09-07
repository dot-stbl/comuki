namespace Comuki.Modules.Projects.Application.Admission;

/// <summary>
/// The answer of <see cref="DomainTypeAdmissionService"/> for one inbound
/// unit of work: may it run, and if yes which control-plane profile takes
/// it. A decision is a value, not an exception — the caller (intake,
/// scheduler, a future endpoint) decides what to do with a denial:
/// park it in the inbox, answer 422, or drop it.
/// </summary>
/// <param name="Admitted">True when the work may enter; <see cref="ProfileKey"/> is then set.</param>
/// <param name="DomainType">Normalized domain-type key the decision was made for.</param>
/// <param name="ProfileKey">Resolved control-plane profile key; null when denied.</param>
/// <param name="Reasons">Stable reason codes; empty when admitted.</param>
public sealed record DomainTypeAdmissionDecision(
    bool Admitted,
    string DomainType,
    string? ProfileKey,
    IReadOnlyList<string> Reasons)
{
    /// <summary>Admitted, routed to <paramref name="profileKey"/>.</summary>
    /// <param name="domainType"></param>
    /// <param name="profileKey"></param>
    /// <returns></returns>
    public static DomainTypeAdmissionDecision Admit(string domainType, string profileKey)
    {
        return new DomainTypeAdmissionDecision(Admitted: true, domainType, profileKey, []);
    }

    /// <summary>Denied for <paramref name="reasons"/> (never empty by construction of the callers).</summary>
    /// <param name="domainType"></param>
    /// <param name="reasons"></param>
    /// <returns></returns>
    public static DomainTypeAdmissionDecision Deny(string domainType, IReadOnlyList<string> reasons)
    {
        return new DomainTypeAdmissionDecision(Admitted: false, domainType, ProfileKey: null, reasons);
    }
}
