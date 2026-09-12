using System.Text;

namespace Comuki.Shared.Bootstrap.Versioning;

/// <summary>
/// Build identity of one comuki binary (issue #56): the assembly version,
/// the git commit sha embedded through <c>SourceRevisionId</c>, the UTC
/// build date and the build mode. Assembled by <see cref="ComukiBuildInfo"/>
/// from the entry assembly's
/// <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/> and
/// the <c>ComukiBuildDate</c> / <c>ComukiBuildMode</c> assembly metadata.
/// </summary>
/// <param name="Version">Assembly version segment (<c>1.0.0</c> in <c>1.0.0+sha</c>).</param>
/// <param name="Revision">Git commit sha appended to the informational version, or null when built outside a repository.</param>
/// <param name="BuildDateUtc">Day-granularity UTC build date, or null when the metadata is absent.</param>
/// <param name="Mode">Build mode (<c>debug</c> / <c>release</c>), lowercased.</param>
public sealed record ComukiBuildInformation(string Version, string? Revision, string? BuildDateUtc, string Mode)
{
    /// <summary>Placeholder used when the assembly carries no version metadata.</summary>
    public static readonly ComukiBuildInformation Unknown = new("0.0.0", null, null, "unknown");

    /// <summary>
    /// Renders the flat comuki version line —
    /// <c>comuki version=1.0.0 sha=… build=2026-09-11 mode=debug</c> —
    /// shared by the <c>version</c> subcommand, the startup banner and
    /// <c>comuki-migrator</c>. Absent segments are omitted.
    /// </summary>
    /// <param name="product">Binary name (<c>comuki</c>, <c>comuki-brain</c>, …).</param>
    public string ToVersionLine(string product)
    {
        var builder = new StringBuilder(product);
        builder.Append(" version=").Append(Version);
        if (Revision is { Length: > 0 } revision)
        {
            builder.Append(" sha=").Append(revision);
        }

        if (BuildDateUtc is { Length: > 0 } buildDate)
        {
            builder.Append(" build=").Append(buildDate);
        }

        builder.Append(" mode=").Append(Mode.ToLowerInvariant());
        return builder.ToString();
    }
}
