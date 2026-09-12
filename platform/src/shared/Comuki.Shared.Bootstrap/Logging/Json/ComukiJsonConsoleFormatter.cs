using System.Globalization;
using System.Text;
using System.Text.Json;
using Comuki.Shared.Bootstrap.Correlation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Comuki.Shared.Bootstrap.Logging.Json;

/// <summary>
/// The JSON renderer behind <c>COMUKI_LOG_FORMAT=json</c> (issue #56 §4):
/// one JSON object per line carrying exactly the fields of the flat comuki
/// line — <c>ts</c>, <c>level</c>, <c>category</c>, <c>message</c>, the
/// structured state fields (stringified), the ambient <c>rid</c> when a
/// correlation id is established (issue #56 §5), and <c>exception</c> when
/// present. The lifetime rewrite applies here too, so a json-run host
/// still logs <c>comuki.host listening addr=…</c>. No ANSI colours —
/// JSON output is machine-facing.
/// </summary>
public sealed class ComukiJsonConsoleFormatter(
    TimeProvider? clock = null,
    ICorrelationIdAccessor? correlation = null) : ConsoleFormatter(FormatterName)
{
    /// <summary>The formatter name registered under <c>AddConsoleFormatter</c>.</summary>
    public const string FormatterName = "comuki-json";

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly ICorrelationIdAccessor? correlation = correlation;

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ComukiJsonConsoleWriter.Write(entry, clock.GetUtcNow(), correlation?.CurrentId, textWriter);
    }
}

/// <summary>Renders one log entry as a single-line JSON object.</summary>
file static class ComukiJsonConsoleWriter
{
    public static void Write<TState>(in LogEntry<TState> entry, DateTimeOffset timestamp, string? requestId, TextWriter writer)
    {
        var category = entry.Category ?? string.Empty;
        var message = entry.Formatter is { } formatter
            ? formatter(entry.State, null) ?? string.Empty
            : entry.State?.ToString() ?? string.Empty;

        if (ComukiConsoleFormatter.TryRewriteLifetime(category, entry.EventId.Id, message, out var rewrittenCategory, out var rewrittenMessage))
        {
            category = rewrittenCategory;
            message = rewrittenMessage;
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("ts", timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("level", ComukiConsoleFormatter.LevelLabel(entry.LogLevel));
            json.WriteString("category", category.ToLowerInvariant());
            json.WriteString("message", message);

            if (entry.State is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                foreach (var field in fields)
                {
                    if (field.Key is "{OriginalFormat}")
                    {
                        continue;
                    }

                    json.WriteString(field.Key, field.Value?.ToString());
                }
            }

            if (requestId is { Length: > 0 } correlationId)
            {
                json.WriteString("rid", correlationId);
            }

            if (entry.Exception is { } exception)
            {
                json.WriteString("exception", exception.ToString());
            }

            json.WriteEndObject();
        }

        writer.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}
