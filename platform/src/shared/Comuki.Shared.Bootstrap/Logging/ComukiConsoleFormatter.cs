using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Comuki.Shared.Bootstrap.Logging;

/// <summary>
/// The <c>comuki</c> console formatter (issue #54): one line per event —
/// <c>2026-09-11T10:00:00.123Z info  category  message  key=val</c> —
/// RFC3339 UTC with milliseconds, lowercase level padded to five
/// characters, the full category lowercased, then structured state as
/// key=val pairs. Exceptions continue on a new line, indented. ANSI
/// colours on level/category are emitted only while stdout is not
/// redirected (<see cref="Console.IsOutputRedirected"/> == false).
/// Microsoft.Hosting.Lifetime messages (Now listening on / Application
/// started / shutting down / Content root path) are rewritten to short
/// comuki.host lines.
/// </summary>
public sealed class ComukiConsoleFormatter(Func<bool>? ansiEnabled = null, TimeProvider? clock = null) : ConsoleFormatter(FormatterName)
{
    /// <summary>The formatter name registered under <c>AddConsoleFormatter</c>.</summary>
    public const string FormatterName = "comuki";

    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly Func<bool> emitAnsi = ansiEnabled ?? (static () => !Console.IsOutputRedirected);

    /// <summary>
    /// The lowercase comuki label of a level (trace/debug/info/warn/error/fatal) —
    /// shared with the JSON renderer so both formats agree on level names.
    /// </summary>
    /// <param name="logLevel">The level to label.</param>
    public static string LevelLabel(LogLevel logLevel)
    {
        return ComukiConsoleWriter.LevelLabel(logLevel);
    }

    /// <summary>
    /// Lifetime-message rewrite shared with the JSON renderer: maps
    /// Microsoft.Hosting.Lifetime events onto the short comuki.host forms.
    /// </summary>
    /// <param name="category">Original category.</param>
    /// <param name="eventId">Original event id.</param>
    /// <param name="message">Original message.</param>
    /// <param name="rewrittenCategory">Rewritten category when it applies.</param>
    /// <param name="rewrittenMessage">Rewritten message when it applies.</param>
    public static bool TryRewriteLifetime(string category, int eventId, string message, out string rewrittenCategory, out string rewrittenMessage)
    {
        return HostingLifetimeRewrite.TryRewrite(category, eventId, message, out rewrittenCategory, out rewrittenMessage);
    }

    /// <inheritdoc />
    public override void Write<TState>(in LogEntry<TState> entry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ComukiConsoleWriter.Write(entry, clock.GetUtcNow(), emitAnsi(), textWriter);
    }
}

/// <summary>Renders one log entry in the comuki line format.</summary>
file static class ComukiConsoleWriter
{
    public static void Write<TState>(in LogEntry<TState> entry, DateTimeOffset timestamp, bool ansi, TextWriter writer)
    {
        var category = entry.Category ?? string.Empty;
        var message = entry.Formatter is { } formatter
            ? formatter(entry.State, null) ?? string.Empty
            : entry.State?.ToString() ?? string.Empty;

        if (HostingLifetimeRewrite.TryRewrite(category, entry.EventId.Id, message, out var rewrittenCategory, out var rewrittenMessage))
        {
            category = rewrittenCategory;
            message = rewrittenMessage;
        }

        writer.Write(timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        writer.Write(' ');
        WriteLevel(writer, entry.LogLevel, ansi);
        writer.Write(' ');
        WriteCategory(writer, category.ToLowerInvariant(), ansi);
        writer.Write(' ');
        writer.Write(' ');
        writer.Write(message);

        if (entry.State is IReadOnlyList<KeyValuePair<string, object?>> fields)
        {
            foreach (var field in fields)
            {
                if (field.Key is "{OriginalFormat}")
                {
                    continue;
                }

                writer.Write(' ');
                writer.Write(' ');
                writer.Write(field.Key);
                writer.Write('=');
                writer.Write(field.Value);
            }
        }

        if (entry.Exception is { } exception)
        {
            writer.WriteLine();
            writer.Write(IndentLines(exception.ToString()));
        }

        writer.WriteLine();
    }

    public static void WriteLevel(TextWriter writer, LogLevel logLevel, bool ansi)
    {
        var level = LevelLabel(logLevel).PadRight(5);
        if (!ansi)
        {
            writer.Write(level);
            return;
        }

        var color = logLevel switch
        {
            LogLevel.Trace or LogLevel.Debug => "\x1b[90m",
            LogLevel.Information => "\x1b[32m",
            LogLevel.Warning => "\x1b[33m",
            LogLevel.Error => "\x1b[31m",
            _ => "\x1b[1;91m",
        };

        writer.Write(color);
        writer.Write(level);
        writer.Write("\x1b[0m");
    }

    public static void WriteCategory(TextWriter writer, string category, bool ansi)
    {
        if (!ansi)
        {
            writer.Write(category);
            return;
        }

        writer.Write("\x1b[36m");
        writer.Write(category);
        writer.Write("\x1b[0m");
    }

    public static string LevelLabel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trace",
            LogLevel.Debug => "debug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "error",
            LogLevel.Critical => "fatal",
            _ => "none",
        };
    }

    public static string IndentLines(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            builder.Append("    ").AppendLine(line);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Rewrites Microsoft.Hosting.Lifetime events (ids 1–4) into short
/// comuki.host lines: listening addr=…, started, stopping,
/// content_root path=….
/// </summary>
file static class HostingLifetimeRewrite
{
    public const string SourceCategory = "Microsoft.Hosting.Lifetime";
    public const string RewrittenCategory = "comuki.host";

    public static bool TryRewrite(string category, int eventId, string message, out string rewrittenCategory, out string rewrittenMessage)
    {
        rewrittenCategory = category;
        rewrittenMessage = message;

        if (!string.Equals(category, SourceCategory, StringComparison.Ordinal))
        {
            return false;
        }

        switch (eventId)
        {
            case 1:
                if (StripPrefix(message, "Now listening on: ") is not { } address)
                {
                    return false;
                }

                rewrittenMessage = $"listening addr={address}";
                break;
            case 2:
                rewrittenMessage = "started";
                break;
            case 3:
                rewrittenMessage = "stopping";
                break;
            case 4:
                if (StripPrefix(message, "Content root path: ") is not { } path)
                {
                    return false;
                }

                rewrittenMessage = $"content_root path={path}";
                break;
            default:
                return false;
        }

        rewrittenCategory = RewrittenCategory;
        return true;
    }

    public static string? StripPrefix(string message, string prefix)
    {
        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..].Trim() : null;
    }
}
