using Comuki.Shared.Bootstrap.Versioning;

namespace Comuki.Shared.Bootstrap.Cli;

/// <summary>
/// The shared comuki CLI surface (issue #56): every binary understands the
/// same <c>version</c> subcommand — <c>comuki version</c>,
/// <c>comuki-brain version</c>, … — printing the flat build line and
/// exiting 0. Handled before any host bootstrap so it works on a bare
/// machine with no config, no database and no environment.
/// </summary>
public static class ComukiCli
{
    /// <summary>The subcommand printing the build information.</summary>
    public const string VersionCommand = "version";

    /// <summary>True when <paramref name="args"/> starts with the named subcommand (case-insensitive).</summary>
    public static bool IsCommand(string[] args, string command)
    {
        return args.Length > 0 && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="args"/> starts with the two-part command <c>command subcommand</c> (case-insensitive).</summary>
    public static bool IsCommand(string[] args, string command, string subcommand)
    {
        return args.Length > 1
            && string.Equals(args[0], command, StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], subcommand, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prints the version line of the running entry assembly and returns the
    /// process exit code 0. The product name prefixes the line
    /// (<c>comuki version=… sha=… build=… mode=…</c>).
    /// </summary>
    /// <param name="product">Binary name shown first (<c>comuki</c>, <c>comuki-migrator</c>, …).</param>
    /// <param name="writer">Output sink; defaults to stdout.</param>
    public static int RunVersion(string product, TextWriter? writer = null)
    {
        var output = writer ?? Console.Out;
        output.WriteLine(ComukiBuildInfo.Read().ToVersionLine(product));
        return 0;
    }
}
