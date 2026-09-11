using System.Text;
using Comuki.Shared.Bootstrap.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Logging;

/// <summary>
/// The comuki console formatter (issue #54): line shape
/// <c>ts level  category  message  key=val</c>, ANSI only when enabled
/// (production ties it to <c>!Console.IsOutputRedirected</c>), lifetime
/// messages rewritten, exceptions indented on a new line.
/// </summary>
public sealed class ComukiConsoleFormatterShould
{
    private static readonly DateTimeOffset fixedTimestamp = new(2026, 9, 11, 10, 0, 0, 123, TimeSpan.Zero);

    [Fact(DisplayName = "Given an info entry with structured state, when written, then the line matches the comuki format")]
    public void RendersComukiLineFormat()
    {
        var line = Render(
            LogLevel.Information,
            "Comuki.Host.Workers.WorkerRuntime",
            new EventId(0),
            "claimed work item",
            new StateField("WorkItemId", "wi-42"));

        line.ShouldBe("2026-09-11T10:00:00.123Z info  comuki.host.workers.workerruntime  claimed work item  WorkItemId=wi-42");
    }

    [Theory(DisplayName = "Given a level, when written, then the label is lowercase and padded to five characters")]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "fatal")]
    public void LevelsAreLowercasePadded(LogLevel logLevel, string label)
    {
        var line = Render(logLevel, "comuki.test", new EventId(0), "m");

        line.ShouldStartWith($"2026-09-11T10:00:00.123Z {label,-5} comuki.test  m");
    }

    [Fact(DisplayName = "Given ANSI enabled, when written, then level and category carry colour codes and the text stays intact")]
    public void AnsiWrapsLevelAndCategory()
    {
        var line = Render(LogLevel.Warning, "Comuki.Host", new EventId(0), "careful", ansi: true);

        line.ShouldStartWith("2026-09-11T10:00:00.123Z \x1b[33mwarn \x1b[0m \x1b[36mcomuki.host\x1b[0m  careful");
    }

    [Fact(DisplayName = "Given ANSI disabled (redirected stdout), when written, then no escape codes appear")]
    public void NoAnsiWhenDisabled()
    {
        var line = Render(LogLevel.Warning, "Comuki.Host", new EventId(0), "careful", ansi: false);

        line.ShouldNotContain('\x1b');
    }

    [Fact(DisplayName = "Given the Now listening on lifetime event, when written, then it becomes comuki.host listening addr=…")]
    public void RewritesNowListening()
    {
        var line = Render(
            LogLevel.Information,
            "Microsoft.Hosting.Lifetime",
            new EventId(1),
            "Now listening on: http://localhost:8080");

        line.ShouldBe("2026-09-11T10:00:00.123Z info  comuki.host  listening addr=http://localhost:8080");
    }

    [Theory(DisplayName = "Given lifetime events 2-4, when written, then the short comuki.host forms appear")]
    [InlineData(2, "Application started. Press Ctrl+C to shut down.", "started")]
    [InlineData(3, "Application is shutting down...", "stopping")]
    [InlineData(4, "Content root path: /app", "content_root path=/app")]
    public void RewritesLifetimeForms(int eventId, string message, string expected)
    {
        var line = Render(LogLevel.Information, "Microsoft.Hosting.Lifetime", new EventId(eventId), message);

        line.ShouldEndWith($"  comuki.host  {expected}");
    }

    [Fact(DisplayName = "Given a non-lifetime category with a lifetime-like event id, when written, then the entry passes through untouched")]
    public void OtherCategoriesPassThrough()
    {
        var line = Render(LogLevel.Information, "Comuki.Host", new EventId(2), "Application started. Press Ctrl+C to shut down.");

        line.ShouldEndWith("  comuki.host  Application started. Press Ctrl+C to shut down.");
    }

    [Fact(DisplayName = "Given an entry with an exception, when written, then the exception follows on a new indented line")]
    public void ExceptionIsIndentedOnNewLine()
    {
        var line = Render(
            LogLevel.Error,
            "comuki.test",
            new EventId(0),
            "boom",
            exception: new InvalidOperationException("outer", new ArgumentException("inner")));

        var lines = line.Split('\n');
        lines.Length.ShouldBeGreaterThanOrEqualTo(2);
        lines[0].ShouldEndWith("  boom");
        lines[1].ShouldStartWith("    System.InvalidOperationException: outer");
        foreach (var text in lines.Skip(1))
        {
            text.ShouldStartWith("    ");
        }
    }

    private static string Render(
        LogLevel logLevel,
        string category,
        EventId eventId,
        string message,
        StateField? stateField = null,
        Exception? exception = null,
        bool ansi = false)
    {
        var fields = new List<KeyValuePair<string, object?>>
        {
            new("{OriginalFormat}", message),
        };
        if (stateField is { } field)
        {
            fields.Add(new(field.Key, field.Value));
        }

        var entry = new LogEntry<List<KeyValuePair<string, object?>>?>(
            logLevel,
            category,
            eventId,
            fields,
            exception,
            static (state, _) => state is null ? string.Empty : state[0].Value?.ToString() ?? string.Empty);

        var formatter = new ComukiConsoleFormatter(() => ansi, FrozenClock.Instance);
        var writer = new StringBuilderWriter();
        formatter.Write(in entry, null, writer);

        return writer.Text.Replace("\r\n", "\n");
    }

    /// <summary>One structured state field handed to <see cref="Render"/>.</summary>
    private sealed record StateField(string Key, object Value);

    /// <summary>Clock pinned to the fixed test timestamp.</summary>
    private sealed class FrozenClock : TimeProvider
    {
        public static readonly FrozenClock Instance = new();

        private FrozenClock()
        {
        }

        public override DateTimeOffset GetUtcNow()
        {
            return fixedTimestamp;
        }
    }

    /// <summary>StringWriter exposing the written text without the trailing newline.</summary>
    private sealed class StringBuilderWriter : TextWriter
    {
        private readonly StringBuilder builder = new();

        public string Text => builder.ToString().TrimEnd('\r', '\n');

        public override Encoding Encoding { get; } = Encoding.UTF8;

        public override void Write(string? value)
        {
            builder.Append(value);
        }

        public override void Write(char value)
        {
            builder.Append(value);
        }
    }
}
