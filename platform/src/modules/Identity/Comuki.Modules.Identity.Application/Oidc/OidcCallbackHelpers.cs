namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Pure-logic helpers for the OIDC handlers. Extracted from
/// <see cref="OidcCallbackHandler"/> so the handler class holds only
/// orchestration (per <c>class-layout-and-tooling.md §1a</c>).
/// </summary>
internal static class OidcCallbackHelpers
{
    /// <summary>Builds the <c>/login?reason=oidc-failed&amp;error=&lt;code&gt;</c> redirect target for a failure path.</summary>
    /// <param name="failureCode">Stable failure code (clients branch on this).</param>
    public static string BuildLoginRedirect(string failureCode)
    {
        return $"/login?reason=oidc-failed&error={Uri.EscapeDataString(failureCode)}";
    }
}
