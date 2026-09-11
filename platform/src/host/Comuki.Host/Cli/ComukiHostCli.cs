using Comuki.Shared.Bootstrap.Cli;

namespace Comuki.Host.Cli;

/// <summary>
/// The <c>comuki</c> operator CLI dispatcher (issue #56). Runs in
/// <c>Program</c> before any host bootstrap — every branch here must work
/// on a bare machine: no config.toml, no database, no logging pipeline.
/// Returns the process exit code for the handled command, or null when the
/// arguments are not a CLI command and the host should boot normally.
/// Build-time OpenAPI generation arrives with empty args and never enters
/// a branch.
/// </summary>
internal static class ComukiHostCli
{
    /// <summary>Handles <c>version</c>; null when the args are not a CLI command.</summary>
    public static int? TryRun(string[] args)
    {
        return ComukiCli.IsCommand(args, ComukiCli.VersionCommand)
            ? ComukiCli.RunVersion("comuki")
            : null;
    }
}
