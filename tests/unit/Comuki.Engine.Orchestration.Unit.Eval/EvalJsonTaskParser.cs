using System.Text.Json;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Parses a single golden task from JSON on disk into an
/// <see cref="EvalTask"/>. The schema is the fixture contract: any
/// deviation is a hard parse failure surfaced through
/// <see cref="EvalParseException"/> — the runner doesn't see it.
/// </summary>
public static class EvalJsonTaskParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Schema version this parser understands — bumped when the JSON shape changes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Reads the file at <paramref name="path"/> and returns the parsed
    /// task. Throws <see cref="EvalParseException"/> with the file path
    /// and the underlying error on any failure (missing file, malformed
    /// JSON, missing required fields, unknown enum values).
    /// </summary>
    /// <param name="path">Absolute path to the golden-task JSON file.</param>
    /// <returns>The parsed task.</returns>
    /// <exception cref="EvalParseException">When the file is missing or malformed.</exception>
    public static EvalTask ParseFile(string path)
    {
        string body;
        try
        {
            body = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            throw new EvalParseException($"could not read '{path}': {exception.Message}", path, exception);
        }

        return Parse(body, path);
    }

    /// <summary>Parses a raw JSON body. <paramref name="source"/> is used in error messages.</summary>
    /// <param name="body">Raw JSON text.</param>
    /// <param name="source">File path or label for error messages.</param>
    /// <returns>The parsed task.</returns>
    /// <exception cref="EvalParseException">When the JSON is malformed or the schema doesn't match.</exception>
    public static EvalTask Parse(string body, string source = "<inline>")
    {
        EvalTaskDocument document;
        try
        {
            document = JsonSerializer.Deserialize<EvalTaskDocument>(body, Options)
                ?? throw new EvalParseException("JSON document deserialized to null", source);
        }
        catch (JsonException exception)
        {
            throw new EvalParseException($"malformed JSON in '{source}': {exception.Message}", source, exception);
        }

        if (document.SchemaVersion != SchemaVersion)
        {
            throw new EvalParseException(
                $"schema version mismatch in '{source}': expected {SchemaVersion}, got {document.SchemaVersion}",
                source);
        }

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new EvalParseException($"task id is missing or empty in '{source}'", source);
        }

        if (string.IsNullOrWhiteSpace(document.Name))
        {
            throw new EvalParseException($"task name is missing or empty in '{source}'", source);
        }

        if (!Enum.TryParse<EvalTaskKind>(document.Kind, ignoreCase: true, out var kind))
        {
            throw new EvalParseException(
                $"unknown task kind '{document.Kind}' in '{source}' (expected: Run | WorkItem)",
                source);
        }

        var operations = (document.Operations ?? [])
            .Select(op => ToOperation(op, source))
            .ToArray();
        if (operations.Length == 0)
        {
            throw new EvalParseException($"task '{document.Id}' has no operations in '{source}'", source);
        }

        var expected = ToExpected(document.Expected, document.Id, source);

        return new EvalTask(
            Id: document.Id,
            Name: document.Name,
            Kind: kind,
            Operations: operations,
            Expected: expected);
    }

    private static EvalOperation ToOperation(EvalOperationDocument document, string source)
    {
        return Enum.TryParse<EvalAction>(document.Action, ignoreCase: true, out var action)
            ? new EvalOperation(action, document.Status ?? string.Empty)
            : throw new EvalParseException(
                $"unknown action '{document.Action}' (expected: Create | Transition | AssignLease | Heartbeat | ReleaseLease | TransitionExpectFailure)",
                source);
    }

    private static EvalExpected ToExpected(EvalExpectedDocument? document, string taskId, string source)
    {
        return document is null
            ? new EvalExpected(string.Empty, [])
            : document.ExpectsFailure
                ? new EvalExpected(
                    FinalStatus: string.Empty,
                    TransitionLog: [],
                    ExpectsFailure: true,
                    ExpectedFailureMessage: document.ExpectedFailureMessage ?? string.Empty)
                : BuildExpectedFromLog(document, taskId, source);
    }

    private static EvalExpected BuildExpectedFromLog(EvalExpectedDocument document, string taskId, string source)
    {
        var log = document.TransitionLog ?? [];
        var finalStatus = document.FinalStatus ?? string.Empty;

        return !string.IsNullOrEmpty(finalStatus) || log.Count > 0
            ? new EvalExpected(FinalStatus: finalStatus, TransitionLog: log)
            : throw new EvalParseException(
                $"task '{taskId}' expected outcome is empty (neither final-status nor transition-log) in '{source}'",
                source);
    }

    private sealed record EvalTaskDocument(
        int SchemaVersion,
        string Id,
        string Name,
        string Kind,
        IReadOnlyList<EvalOperationDocument>? Operations,
        EvalExpectedDocument? Expected);

    private sealed record EvalOperationDocument(string Action, string? Status);

    private sealed record EvalExpectedDocument(
        string? FinalStatus,
        IReadOnlyList<string>? TransitionLog,
        bool ExpectsFailure = false,
        string? ExpectedFailureMessage = null);
}

/// <summary>
/// Raised by <see cref="EvalJsonTaskParser"/> when a golden task file
/// is missing, malformed, or doesn't match the schema. Carries the
/// source label so the message stays actionable in the test output.
/// </summary>
public sealed class EvalParseException : Exception
{
    /// <summary>File path or label the parser was reading from.</summary>
    public string Origin { get; }

    /// <inheritdoc />
    public EvalParseException(string message, string origin, Exception? inner = null)
        : base(message, inner)
    {
        Origin = origin;
    }
}
