using System.Text;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Verify.Infrastructure.Sync;

/// <summary>
/// Stream-ordered stdout+stderr capture with a soft character cap. The
/// cap is enforced on <see cref="Snapshot"/>: when the running total
/// exceeds it, the head is dropped (the tail is what the operator
/// cares about — the failure usually happens at the end). The first
/// call past the cap logs a single Warning.
/// </summary>
internal sealed class OutputCapture(int cap, ILogger logger)
{
    private readonly StringBuilder buffer = new(capacity: Math.Min(cap * 2, 1 << 16));
    private bool truncationLogged;

    /// <summary>Append one line from stdout or stderr.</summary>
    /// <param name="line">The captured line, or null when the stream signalled end-of-output.</param>
    /// <param name="isError">True for stderr, false for stdout — controls the marker prefix.</param>
    public void AppendLine(string? line, bool isError)
    {
        if (line is null)
        {
            return;
        }

        buffer.Append(isError ? "[err] " : "[out] ").Append(line).Append('\n');
    }

    /// <summary>Returns the captured log, dropping the head if past the cap.</summary>
    /// <returns>The (possibly truncated) captured output.</returns>
    public string Snapshot()
    {
        var raw = buffer.ToString();
        if (raw.Length <= cap)
        {
            return raw;
        }

        if (!truncationLogged)
        {
            logger.LogWarning(
                "generic-command output log exceeded {Cap} chars; truncating head, keeping tail",
                cap);
            truncationLogged = true;
        }

        return "[truncated: head dropped]\n" + raw[^cap..];
    }
}
