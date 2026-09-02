using System.Security.Cryptography;
using System.Text;

namespace Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;

/// <summary>
/// Yandex Tracker webhook verification: Tracker webhooks carry no
/// cryptographic signature — the deployment configures the hook to send
/// a shared-secret header (default <c>X-Tracker-Token</c>, name
/// overridable in the connection settings) which is compared fixed-time
/// against the connection's secret.
/// </summary>
public static class YandexTrackerWebhookVerifier
{
    /// <summary>Compares the secret header against the expected secret.</summary>
    /// <param name="secret">The webhook secret; null/empty fails closed.</param>
    /// <param name="secretHeader">The raw header value.</param>
    /// <returns></returns>
    public static bool Verify(string? secret, string? secretHeader)
    {
        return !string.IsNullOrEmpty(secret) && secretHeader is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(secretHeader),
            Encoding.UTF8.GetBytes(secret));
    }
}
