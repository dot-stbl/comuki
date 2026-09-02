using System.Security.Cryptography;
using System.Text;

namespace Comuki.Modules.Intake.Infrastructure.Providers.Jira;

/// <summary>
/// Jira webhook verification: Jira webhooks carry no HMAC — the
/// deployment appends a shared secret to the hook URL as a query
/// parameter (the documented pattern) which is compared fixed-time
/// against the connection's secret.
/// </summary>
public static class JiraWebhookVerifier
{
    /// <summary>Compares the secret query parameter against the expected secret.</summary>
    /// <param name="secret">The webhook secret; null/empty fails closed.</param>
    /// <param name="secretParam">The raw query parameter value.</param>
    /// <returns></returns>
    public static bool Verify(string? secret, string? secretParam)
    {
        return !string.IsNullOrEmpty(secret) && secretParam is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(secretParam),
            Encoding.UTF8.GetBytes(secret));
    }
}
