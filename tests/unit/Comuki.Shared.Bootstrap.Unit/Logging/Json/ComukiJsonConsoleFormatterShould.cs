using System.Text;
using System.Text.Json;
using Comuki.Shared.Bootstrap.Logging.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Logging.Json;

/// <summary>
/// The JSON console renderer (issue #56 §4): one JSON object per line with
/// the same fields as the flat comuki line — ts/level/category/message,
/// structured state as stringified fields, the lifetime rewrite applied,
/// and the exception carried as a field.
/// </summary>
public sealed class ComukiJsonConsoleFormatterShould
{
    private static readonly DateTimeOffset fixedTimestamp = new(2026, 9, 11, 10, 0, 0, 123, TimeSpan.Zero);

    [Fact(DisplayName = "Given an info entry with structured state, when written, then the line is one JSON object with the comuki fields")]
    public void RendersComukiFieldsAsJson()
    {
        var line = Render(
            LogLevel.Information,
            "Comuki.Host.Workers.WorkerRuntime",
            new EventId(0),
            "claimed work item",
            new StateField("WorkItemId", "wi-42"));

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        root.GetProperty("ts").GetString().ShouldBe("2026-09-11T10:00:00.123Z");
        root.GetProperty("level").GetString().ShouldBe("info");
        root.GetProperty("category").GetString().ShouldBe("comuki.host.workers.workerruntime");
        root.GetProperty("message").GetString().ShouldBe("claimed work item");
        root.GetProperty("WorkItemId").GetString().ShouldBe("wi-42");
    }

    [Fact(DisplayName = "Given the Now listening on lifetime event, when written, then the comuki.host rewrite applies in json too")]
    public void RewritesLifetimeEvents()
    {
        var line = Render(
            LogLevel.Information,
            "Microsoft.Hosting.Lifetime",
            new EventId(1),
            "Now listening on: http://localhost:8080");

        using var document = JsonDocument.Parse(line);
        document.RootElement.GetProperty("category").GetString().ShouldBe("comuki.host");
        document.RootElement.GetProperty("message").GetString().ShouldBe("listening addr=http://localhost:8080");
    }

    [Fact(DisplayName = "Given an entry with an exception, when written, then the exception rides as a field and the line stays single")]
    public void ExceptionRidesAsField()
    {
        var line = Render(
            LogLevel.Error,
            "comuki.test",
            new EventId(0),
            "boom",
            exception: new InvalidOperationException("outer"));

        line.ShouldNotContain('\n');
        using var document = JsonDocument.Parse(line);
        document.RootElement.GetProperty("exception").GetString().ShouldNotBeNull().ShouldContain("System.InvalidOperationException: outer");
    }

    [Fact(DisplayName = "Given a message with characters needing escaping, when written, then the JSON stays a single parseable line")]
    public void EscapesSpecialCharacters()
    {
        var line = Render(LogLevel.Information, "comuki.test", new EventId(0), "quote \" newline \\ end");

        using var document = JsonDocument.Parse(line);
        document.RootElement.GetProperty("message").GetString().ShouldBe("quote \" newline \\ end");
    }

    private static string Render(
        LogLevel logLevel,
        string category,
        EventId eventId,
        string message,
        StateField? stateField = null,
        Exception? exception = null)
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

        var formatter = new ComukiJsonConsoleFormatter(FrozenClock.Instance);
        var writer = new StringBuilderWriter();
        formatter.Write(in entry, null, writer);

        return writer.Text;
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

        public override void WriteLine(string? value)
        {
            builder.Append(value).Append('\n');
        }
    }
}
