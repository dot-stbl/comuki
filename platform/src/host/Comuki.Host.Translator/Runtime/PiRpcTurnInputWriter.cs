using System.Text;
using System.Text.Json;

namespace Comuki.Host.Translator.Runtime;

/// <summary>
/// Stdin-side writer for the <c>pi --mode rpc</c> channel. Each
/// command is one JSON object on its own line, terminated by LF
/// and flushed. A single lock guards the
/// <see cref="StreamWriter"/> so a concurrent steer and the
/// reader task's flush (or two steers) can't interleave mid-line.
/// <see cref="CloseStdin"/> is the orderly-shutdown close (the
/// session's dispose calls it; pi's
/// <c>openspec/changes/add-orchestra/spike-1b-report.md</c>
/// documents that closing stdin makes pi exit code 0).
/// </summary>
internal sealed class PiRpcTurnInputWriter : ITurnInputWriter
{
    // pi's stdin JSON-RPC payload: { type, id, message } — three
    // single-word keys. Both camelCase (Web) and snake_case_lower
    // produce the same wire output for single-word field names; the
    // frozen `Web` singleton is the canonical choice here so the
    // writer doesn't carry a hand-rolled `JsonSerializerOptions`
    // when one isn't needed.
    private readonly StreamWriter writer;
    private readonly ILogger logger;
    private readonly Lock writeGate = new();
    private bool closed;

    public PiRpcTurnInputWriter(Stream stdin, ILogger logger)
    {
        writer = new StreamWriter(stdin, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
            NewLine = "\n",
        };
        this.logger = logger;
    }

    public bool TryWriteSteer(string turnId, string text)
    {
        return TryWriteCommand(new { type = "steer", id = turnId, message = text });
    }

    public bool TryWriteFollowUp(string turnId, string text)
    {
        return TryWriteCommand(new { type = "follow_up", id = turnId, message = text });
    }

    public bool TryWritePrompt(string turnId, string text)
    {
        return TryWriteCommand(new { type = "prompt", id = turnId, message = text });
    }

    /// <summary>Closes the underlying <see cref="StreamWriter"/> (pi's orderly-shutdown signal).</summary>
    public void CloseStdin()
    {
        lock (writeGate)
        {
            if (closed)
            {
                return;
            }

            try
            {
                writer.Flush();
            }
            catch (Exception)
            {
                // best-effort: the close below is the actual shutdown signal
            }

            try
            {
                writer.Dispose();
            }
            catch (Exception)
            {
                // ignore — the process may already have died
            }

            closed = true;
        }
    }

    private bool TryWriteCommand(object command)
    {
        lock (writeGate)
        {
            if (closed)
            {
                return false;
            }

            try
            {
                // pi's stdin is JSON-RPC: one full JSON object per line.
                // We serialize to the underlying BaseStream (UTF-8 bytes
                // immediately) and keep the StreamWriter for the trailing
                // LF + flush. Writes hit the OS pipe in order: JSON bytes
                // immediately on BaseStream, '\n' on the next
                // writer.Flush(). The pi wire parser expects exactly this
                // — a single LF-terminated JSON object per command.
                JsonSerializer.Serialize(writer.BaseStream, command, JsonSerializerOptions.Web);
                writer.Write('\n');
                writer.Flush();
                return true;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to write {CommandType} command to pi stdin",
                    command.GetType().Name);
                return false;
            }
        }
    }
}
