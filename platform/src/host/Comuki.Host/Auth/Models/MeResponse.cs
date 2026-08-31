using Comuki.Modules.Identity.Application.Authorization;

namespace Comuki.Host.Auth.Models;

/// <summary>
/// The authenticated caller's identity for SPA session bootstrap:
/// who the principal resolves to, the roles it holds, and the
/// effective permission sets per scope. Permissions are computed for
/// the request's subject — an API-key request reports the key's
/// assignments, not its owner's.
/// </summary>
public sealed record MeResponse
{
    /// <summary>The owning user account id; null when the principal carries none.</summary>
    public required Guid? UserId { get; init; }

    /// <summary>Stable subject-type key: <c>user</c> or <c>api-key</c>.</summary>
    public required string SubjectType { get; init; }

    /// <summary>The subject permissions are computed for (user id or api key id).</summary>
    public required Guid SubjectId { get; init; }

    /// <summary>Email claim when the session is a user cookie.</summary>
    public string? Email { get; init; }

    /// <summary>Display-name claim (user name, or the api key's label).</summary>
    public string? DisplayName { get; init; }

    /// <summary>Keys of the active role assignments, distinct and ordered.</summary>
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>Effective permissions split by scope.</summary>
    public required PermissionsView Permissions { get; init; }

    /// <summary>Platform-wide and per-project permission keys.</summary>
    /// <param name="Platform"></param>
    /// <param name="Projects"></param>
    public sealed record PermissionsView(
        IReadOnlyList<string> Platform,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Projects)
    {
        /// <summary>Flattens a <see cref="SubjectAuthorization"/> into ordered wire strings.</summary>
        /// <param name="authorization"></param>
        /// <returns></returns>
        public static PermissionsView From(SubjectAuthorization authorization)
        {
            return new PermissionsView(
                [.. authorization.PlatformPermissions.Select(static key => key.Value).Order(StringComparer.Ordinal)],
                authorization.ProjectPermissions.ToDictionary(
                    static pair => pair.Key.Value.ToString(),
                    static pair => (IReadOnlyList<string>)[.. pair.Value.Select(static key => key.Value).Order(StringComparer.Ordinal)]));
        }
    }
}
