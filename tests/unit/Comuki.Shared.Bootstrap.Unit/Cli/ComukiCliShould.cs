using System.Text;
using Comuki.Shared.Bootstrap.Cli;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Cli;

/// <summary>
/// The shared version subcommand (issue #56): recognised case-insensitively
/// as args[0], not recognised as a later argument or a flag, and prints the
/// product-prefixed flat version line with exit code 0.
/// </summary>
public sealed class ComukiCliShould
{
    [Theory(DisplayName = "Given args starting with the command, when checked, then it is recognised")]
    [InlineData("version")]
    [InlineData("VERSION")]
    [InlineData("Version")]
    public void RecogniseVersionCommand(string firstArgument)
    {
        ComukiCli.IsCommand([firstArgument], ComukiCli.VersionCommand).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given args that are not the command first, when checked, then it is not recognised")]
    [InlineData("version", "--extra")]
    [InlineData("--version")]
    [InlineData("run")]
    public void IgnoreNonLeadingOrFlagArguments(params string[] args)
    {
        ComukiCli.IsCommand(args, ComukiCli.VersionCommand).ShouldBe(args[0] == "version");
    }

    [Fact(DisplayName = "Given no args, when checked, then no command is recognised")]
    public void IgnoreEmptyArgs()
    {
        ComukiCli.IsCommand([], ComukiCli.VersionCommand).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given the version command, when run, then the product version line is printed and 0 returned")]
    public void PrintVersionLineAndReturnZero()
    {
        var writer = new StringBuilderWriter();

        var exitCode = ComukiCli.RunVersion("comuki-test", writer);

        exitCode.ShouldBe(0);
        writer.Text.ShouldStartWith("comuki-test version=");
        writer.Text.ShouldContain(" mode=");
        writer.Text.ShouldEndWith("\n");
    }

    /// <summary>StringWriter exposing the written text verbatim.</summary>
    private sealed class StringBuilderWriter : TextWriter
    {
        private readonly StringBuilder builder = new();

        public string Text => builder.ToString();

        public override Encoding Encoding { get; } = Encoding.UTF8;

        public override void Write(string? value)
        {
            builder.Append(value);
        }

        public override void WriteLine(string? value)
        {
            builder.Append(value).Append('\n');
        }
    }
}
