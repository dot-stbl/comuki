using System.Text;

namespace Comuki.Host.Unit.Cli;

/// <summary>Line-capturing TextWriter for CLI output assertions.</summary>
internal sealed class LineWriter : TextWriter
{
    private readonly StringBuilder builder = new();

    /// <summary>The captured lines, without trailing newline separators.</summary>
    public IReadOnlyList<string> Lines => builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <inheritdoc />
    public override Encoding Encoding { get; } = Encoding.UTF8;

    /// <inheritdoc />
    public override void Write(string? value)
    {
        builder.Append(value);
    }

    /// <inheritdoc />
    public override void WriteLine(string? value)
    {
        builder.Append(value).Append('\n');
    }
}
