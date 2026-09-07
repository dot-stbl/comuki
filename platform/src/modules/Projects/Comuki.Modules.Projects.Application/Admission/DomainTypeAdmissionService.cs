using Comuki.Modules.Projects.Application.DomainTypes;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Admission;

/// <summary>
/// The domain-user intake gate: given a project, a user-facing domain type
/// and the intake source the work arrived from, answers whether it may run
/// and which control-plane profile takes it.
/// <para>
/// Two layers, in order. First <b>admission</b> — the per-project
/// <see cref="DomainTypeAdmission"/> policy (enabled? blocked? source
/// allowed?). Then <b>routing</b> — <see cref="IProjectDomainTypeResolver"/>
/// maps the domain type onto a profile key. A project with no policy row
/// for the domain type falls back to its declared
/// <see cref="ProjectSettings.DomainType"/> mode: <c>Custom</c> declared
/// "only what I mapped", so an unregistered domain type is denied;
/// <c>Standard</c> / <c>Hybrid</c> have a working default profile and stay
/// open — adding this gate must not silently stop existing projects.
/// </para>
/// Scoped: the admission store is one EF context per unit of work; the
/// settings store and the resolver are singletons.
/// </summary>
/// <param name="settingsStore">Live-reloading per-project settings (routing mode + JSON map).</param>
/// <param name="admissionStore">Per-project domain-type admission policies.</param>
/// <param name="resolver">Domain-type → profile-key routing.</param>
public sealed class DomainTypeAdmissionService(
    IProjectSettingsStore settingsStore,
    IDomainTypeAdmissionStore admissionStore,
    IProjectDomainTypeResolver resolver)
{
    /// <summary>Reason code reported when the domain type is blank.</summary>
    public const string EmptyDomainTypeReason = "empty_domain_type";

    /// <summary>Reason code reported when the project has no settings row (unknown or out of scope).</summary>
    public const string SettingsMissingReason = "project_settings_missing";

    /// <summary>Reason code reported when a Custom project declared no policy for the domain type.</summary>
    public const string UnregisteredReason = "unregistered_domain_type";

    /// <summary>Prefix of reason codes that come from the routing resolver (<c>profile_missing</c>, …).</summary>
    public const string ProfileReasonPrefix = "profile_";

    /// <summary>
    /// Evaluates admission plus routing for one inbound unit of work.
    /// </summary>
    /// <param name="projectId">Project the work is attributed to.</param>
    /// <param name="domainType">User-facing domain type (<c>code</c>, <c>data</c>, …); normalized here.</param>
    /// <param name="source">Intake source key (<c>github</c>, <c>gitlab</c>, <c>native</c>, …).</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Admitted decision carrying the profile key, or a denial carrying reason codes.</returns>
    public async Task<DomainTypeAdmissionDecision> EvaluateAsync(
        ProjectId projectId,
        string domainType,
        string source,
        CancellationToken cancellationToken = default)
    {
        var normalized = DomainTypeAdmission.NormalizeKey(domainType);
        if (normalized.Length == 0)
        {
            return DomainTypeAdmissionDecision.Deny(normalized, [EmptyDomainTypeReason]);
        }

        if (await settingsStore.FindAsync(projectId, cancellationToken) is not { } settings)
        {
            return DomainTypeAdmissionDecision.Deny(normalized, [SettingsMissingReason]);
        }

        var policy = await admissionStore.FindAsync(projectId, normalized, cancellationToken);
        var denials = policy?.EvaluateDenials(source)
            ?? DomainTypeAdmissionSteps.MissingPolicyDenials(settings.DomainType);

        return denials.Count > 0
            ? DomainTypeAdmissionDecision.Deny(normalized, denials)
            : DomainTypeAdmissionSteps.RouteProfile(resolver, settings, normalized);
    }
}

/// <summary>
/// The two steps <see cref="DomainTypeAdmissionService"/> composes, as pure
/// functions. File-scoped so the service keeps one public method and no
/// private helpers (<c>code-shape.md</c> §9).
/// </summary>
file static class DomainTypeAdmissionSteps
{
    /// <summary>
    /// Default denials for a domain type the project declared no policy for:
    /// closed for <see cref="ProjectDomainType.Custom"/>, open otherwise.
    /// </summary>
    /// <param name="mode"></param>
    /// <returns></returns>
    public static IReadOnlyList<string> MissingPolicyDenials(ProjectDomainType mode)
    {
        return mode is ProjectDomainType.Custom
            ? [DomainTypeAdmissionService.UnregisteredReason]
            : [];
    }

    /// <summary>
    /// Routes an admitted domain type to its profile key. The resolver
    /// reports an unmappable domain type as a typed exception; this is the
    /// boundary that translates it into the admission contract (a decision),
    /// so the caller never has to catch — the reason rides along as
    /// <c>profile_missing</c> / <c>profile_malformed</c> / <c>profile_empty</c>.
    /// </summary>
    /// <param name="resolver"></param>
    /// <param name="settings"></param>
    /// <param name="domainType"></param>
    /// <returns></returns>
    public static DomainTypeAdmissionDecision RouteProfile(
        IProjectDomainTypeResolver resolver,
        ProjectSettings settings,
        string domainType)
    {
        try
        {
            return DomainTypeAdmissionDecision.Admit(domainType, resolver.ResolveProfileKey(settings, domainType));
        }
        catch (ProjectDomainTypeNotMappedException exception)
        {
            return DomainTypeAdmissionDecision.Deny(
                domainType,
                [$"{DomainTypeAdmissionService.ProfileReasonPrefix}{exception.Reason}"]);
        }
    }
}
