using System.ComponentModel.DataAnnotations;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Worker token settings. The pepper defaults to a dev-only value unless the
/// <c>COMUKI_TOKEN_PEPPER</c> environment variable overrides it — production
/// must always set the variable.
/// </summary>
public sealed class WorkerTokenOptions
{
    public const string SectionName = "Security:WorkerToken";

    /// <summary>Environment variable overriding the dev pepper in production.</summary>
    public const string PepperEnvironmentVariable = "COMUKI_TOKEN_PEPPER";

    private const string DevDefaultPepper = "comuki-dev-only-pepper-override-in-production";

    /// <summary>HMAC-SHA256 pepper applied before storing a token hash.</summary>
    [Required]
    [MinLength(16)]
    public string Pepper { get; init; } =
        Environment.GetEnvironmentVariable(PepperEnvironmentVariable) is { Length: > 0 } pepper
            ? pepper
            : DevDefaultPepper;

    /// <summary>Token lifetime used when <see cref="WorkerTokenIssuer.Issue"/> gets no explicit TTL.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "24:00:00")]
    public TimeSpan TokenTtl { get; init; } = TimeSpan.FromMinutes(15);
}
