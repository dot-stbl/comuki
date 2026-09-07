namespace Comuki.Modules.Projects.Application.DomainTypes;

/// <summary>
/// Raised by <see cref="IProjectDomainTypeResolver"/> when the project's
/// declared <see cref="Domain.Settings.ProjectDomainType"/> mode cannot
/// resolve the requested domain type: a Custom project without the domain
/// in its JSON map, a Hybrid project missing from both the JSON map and
/// the default, or a malformed JSON map. Maps to HTTP 422 by the host
/// error handler (semantic error, not an upstream failure).
/// </summary>
/// <param name="domainType">The user-facing domain type that was requested.</param>
/// <param name="projectDomainType">The project's declared routing mode.</param>
/// <param name="reason">Short, stable reason: <c>missing</c>, <c>malformed</c>, <c>empty</c>.</param>
public sealed class ProjectDomainTypeNotMappedException(
    string domainType,
    string projectDomainType,
    string reason)
    : Exception(
        $"domain type '{domainType}' is not mapped under project mode '{projectDomainType}' ({reason})")
{
    /// <summary>The user-facing domain type that was requested.</summary>
    public string DomainType { get; } = domainType;

    /// <summary>The project's declared routing mode.</summary>
    public string ProjectDomainType { get; } = projectDomainType;

    /// <summary>Short, stable reason: <c>missing</c>, <c>malformed</c>, <c>empty</c>.</summary>
    public string Reason { get; } = reason;
}
