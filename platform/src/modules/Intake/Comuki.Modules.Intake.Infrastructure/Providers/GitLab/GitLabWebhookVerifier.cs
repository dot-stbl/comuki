using System.Security.Cryptography;
using System.Text;

namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>
/// GitLab webhook verification: the <c>X-Gitlab-Token</c> header must
/// equal the configured secret token — compared fixed-time so a
/// mismatch leaks no timing.
/// </summary>
public static class GitLabWebhookVerifier
{
    /// <summary>Compares the token header against the expected secret.</summary>
    /// <param name="secret">The webhook token; null/empty fails closed.</param>
    /// <param name="tokenHeader">The raw header value.</param>
    /// <returns></returns>
    public static bool Verify(string? secret, string? tokenHeader)
    {
        return !string.IsNullOrEmpty(secret) && tokenHeader is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(tokenHeader),
            Encoding.UTF8.GetBytes(secret));
    }
}
