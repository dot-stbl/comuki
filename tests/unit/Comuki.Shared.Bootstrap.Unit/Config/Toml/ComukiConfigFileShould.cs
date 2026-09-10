using Comuki.Shared.Bootstrap.Config.Toml;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Config.Toml;

/// <summary>
/// config.toml search chain (issue #54): COMUKI_CONFIG_PATH →
/// ./config.toml (cwd) → /etc/comuki/config.toml (Linux only) → null.
/// The pure core is tested through injected lookups and file probes —
/// no real files touched.
/// </summary>
public sealed class ComukiConfigFileShould
{
    [Fact(DisplayName = "Given COMUKI_CONFIG_PATH pointing at an existing file, when Find runs, then it wins")]
    public void ExplicitPathWins()
    {
        var found = ComukiConfigFile.Find(
            static name => name == ComukiConfigFile.PathEnvironmentVariable ? "/explicit/config.toml" : null,
            static () => "/cwd",
            static () => false,
            static path => path == "/explicit/config.toml");

        found.ShouldBe("/explicit/config.toml");
    }

    [Fact(DisplayName = "Given a blank COMUKI_CONFIG_PATH and config.toml in cwd, when Find runs, then the cwd file is found")]
    public void WorkingDirectoryIsProbed()
    {
        var workingDirectory = @"C:\somewhere\app";

        var found = ComukiConfigFile.Find(
            static name => name == ComukiConfigFile.PathEnvironmentVariable ? " " : null,
            static () => @"C:\somewhere\app",
            static () => false,
            static path => path == @"C:\somewhere\app\config.toml");

        found.ShouldBe(Path.Combine(workingDirectory, "config.toml"));
    }

    [Fact(DisplayName = "Given no explicit path and no cwd file on Linux, when Find runs, then /etc/comuki/config.toml applies")]
    public void LinuxSystemPathIsProbed()
    {
        var found = ComukiConfigFile.Find(
            static _ => null,
            static () => "/cwd",
            isLinux: static () => true,
            static path => path == "/etc/comuki/config.toml");

        found.ShouldBe(ComukiConfigFile.LinuxSystemPath);
    }

    [Fact(DisplayName = "Given /etc exists only on Linux, when Find runs off-Linux, then the system path is not probed")]
    public void SystemPathSkippedOffLinux()
    {
        var found = ComukiConfigFile.Find(
            static _ => null,
            static () => "/cwd",
            static () => false,
            static path => path == "/etc/comuki/config.toml");

        found.ShouldBeNull();
    }

    [Fact(DisplayName = "Given no candidates anywhere, when Find runs, then null comes back — empty config, no error")]
    public void NothingFoundReturnsNull()
    {
        var found = ComukiConfigFile.Find(
            static _ => null,
            static () => "/cwd",
            static () => true,
            static _ => false);

        found.ShouldBeNull();
    }
}
