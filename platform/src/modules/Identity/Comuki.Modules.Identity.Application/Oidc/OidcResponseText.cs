namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Pure-logic helpers for the OIDC handlers. Extracted from
/// <see cref="OidcTokenExchange"/> so the handler class holds only
/// orchestration (per <c>class-layout-and-tooling.md §1a</c>).
/// </summary>
internal static class OidcResponseText
{
    /// <summary>Truncates a string body to <paramref name="max"/> characters, appending an ellipsis when truncated.</summary>
    /// <param name="value">String to truncate.</param>
    /// <param name="max">Maximum length to keep before truncation.</param>
    public static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max), "…");
    }
}
