using Comuki.Host.Cli.Init;
using Comuki.Shared.Bootstrap;
using Comuki.Shared.Bootstrap.Cli;
using Comuki.Shared.Bootstrap.Config;

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
    /// <summary>The async <c>doctor</c> command — <c>Program</c> awaits it directly (see <see cref="ComukiInit"/> sibling checks).</summary>
    public const string DoctorCommand = "doctor";

    /// <summary>True when the arguments ask for the (async) doctor command.</summary>
    public static bool IsDoctorRequested(string[] args)
    {
        return ComukiCli.IsCommand(args, DoctorCommand);
    }
    /// <summary>Handles <c>version</c> / <c>config show</c> / <c>doctor</c> / <c>init</c>; null when the args are not a CLI command.</summary>
    public static int? TryRun(string[] args)
    {
        return ComukiHostCliRouter.Route(args, Console.Out);
    }

    /// <summary>Prints the effective configuration (config.toml + COMUKI_* env) with secrets masked.</summary>
    internal static int RunConfigShow(TextWriter writer)
    {
        var configuration = new ConfigurationBuilder()
            .UseComukiConfiguration()
            .Build();

        foreach (var line in ComukiConfigView.Render((ConfigurationRoot)configuration))
        {
            writer.WriteLine(line);
        }

        return 0;
    }

    /// <summary>Writes the first-run config.toml and .env skeleton; see <see cref="ComukiInit"/>.</summary>
    internal static int RunInit(string[] args, TextWriter writer)
    {
        return ComukiInit.Run(args, writer);
    }
}

/// <summary>Command routing of the comuki operator CLI: first argument selects the command.</summary>
file static class ComukiHostCliRouter
{
    public static int? Route(string[] args, TextWriter writer)
    {
        return args switch
        {
            [var command, ..] when Matches(command, ComukiCli.VersionCommand) => ComukiCli.RunVersion("comuki", writer),
            [var command, var subcommand, ..] when Matches(command, "config") && Matches(subcommand, "show")
                => ComukiHostCli.RunConfigShow(writer),
            [var command, ..] when Matches(command, "init") => ComukiHostCli.RunInit(args, writer),
            _ => null,
        };
    }

    public static bool Matches(string value, string command)
    {
        return string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
    }
}
