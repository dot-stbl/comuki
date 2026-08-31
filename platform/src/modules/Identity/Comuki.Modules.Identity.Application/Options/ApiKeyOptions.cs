using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Identity.Application.Options;

/// <summary>
/// API-key hashing settings. The pepper defaults to a dev-only value
/// unless the <c>COMUKI_IDENTITY_APIKEY_PEPPER</c> environment variable
/// overrides it — production must always set the variable. Rotating the
/// pepper invalidates every stored key HMAC, by design.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>Configuration section (used when a host binds the section).</summary>
    public const string SectionName = "Security:ApiKey";

    /// <summary>Environment variable overriding the dev pepper in production.</summary>
    public const string PepperEnvironmentVariable = "COMUKI_IDENTITY_APIKEY_PEPPER";

    private const string DevDefaultPepper = "comuki-dev-only-apikey-pepper-override-in-production";

    /// <summary>Server-side pepper for the HMAC-SHA256 of stored key tokens.</summary>
    [Required]
    [MinLength(16)]
    public string Pepper { get; init; } =
        Environment.GetEnvironmentVariable(PepperEnvironmentVariable) is { Length: > 0 } pepper
            ? pepper
            : DevDefaultPepper;

    /// <summary>Minimum age before the <c>last_used</c> column is refreshed on authentication.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan LastUsedRefreshInterval { get; init; } = TimeSpan.FromMinutes(5);
}
