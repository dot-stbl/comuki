using Comuki.Shared.Bootstrap.Versioning;

namespace Comuki.Host.Versioning;

/// <summary>
/// Build-info payload of <see cref="ApiRoutes.Version"/> (issue #56 §6):
/// the same identity the <c>version</c> subcommand prints — version, git
/// sha, UTC build date, build mode — for operators and the dashboard
/// footer. Anonymous by design, exactly like <c>/health</c>.
/// </summary>
/// <param name="Version">Assembly version segment.</param>
/// <param name="Sha">Git commit sha, or null when built outside a repository.</param>
/// <param name="BuildDate">Day-granularity UTC build date, or null.</param>
/// <param name="Mode">Lowercased build mode (debug/release).</param>
public sealed record VersionResponse(string Version, string? Sha, string? BuildDate, string Mode)
{
    /// <summary>Maps the bootstrap build information onto the wire payload.</summary>
    /// <param name="information">Build information of the host assembly.</param>
    public static VersionResponse From(ComukiBuildInformation information)
    {
        return new VersionResponse(information.Version, information.Revision, information.BuildDateUtc, information.Mode.ToLowerInvariant());
    }
}
