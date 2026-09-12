using Comuki.Host.Cli;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Cli;

/// <summary>
/// The <c>comuki</c> CLI dispatcher (issue #56): known commands run and
/// return an exit code, unknown args fall through to a normal host boot
/// (null). <c>config show</c> prints the effective toml+env layers with
/// secrets masked.
/// </summary>
[Collection(nameof(CliEnvSafeCollection))]
public sealed class ComukiHostCliShould
{
    [Fact(DisplayName = "Given the version command, when dispatched, then it is handled with exit code 0")]
    public void HandleVersionCommand()
    {
        var writer = new LineWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            ComukiHostCli.TryRun(["version"]).ShouldBe(0);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact(DisplayName = "Given the config show command, when dispatched, then masked effective config lines are printed")]
    public void HandleConfigShowCommand()
    {
        using var toml = TempTomlScope.Install("""
            [server]
            port = 18080

            [security.apikey]
            pepper = "pepper-value-from-toml"
            """);

        var writer = new LineWriter();
        var exitCode = ComukiHostCli.RunConfigShow(writer);

        exitCode.ShouldBe(0);
        writer.Lines.ShouldContain("security:apikey:pepper = ****");
        writer.Lines.ShouldContain("server:port = 18080");
        writer.Lines.ShouldBeInOrder();
    }

    [Fact(DisplayName = "Given an unknown command or empty args, when dispatched, then the host boots normally")]
    public void FallThroughForUnknownCommands()
    {
        ComukiHostCli.TryRun([]).ShouldBeNull();
        ComukiHostCli.TryRun(["serve"]).ShouldBeNull();
        ComukiHostCli.TryRun(["--urls", "http://localhost:8080"]).ShouldBeNull();
    }
}
