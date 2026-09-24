using Comuki.AgentEval.Judges;

namespace Comuki.AgentEval.Unit;

/// <summary>
/// Test double for <see cref="ILlmJudgeClient"/> used by
/// <see cref="LlmJudgeShould"/>. Two construction modes: a fixed
/// response string (returned verbatim from every
/// <see cref="CompleteAsync"/> call), or a fixed exception (thrown
/// from every call). No real network, no real HTTP client — pure
/// in-memory fakes.
/// </summary>
public sealed class FakeLlmJudgeClient : ILlmJudgeClient
{
    private readonly string? cannedResponse;
    private readonly Exception? throwException;

    /// <summary>Builds a fake that always returns <paramref name="response"/>.</summary>
    public FakeLlmJudgeClient(string response)
    {
        cannedResponse = response;
        throwException = null;
    }

    /// <summary>Builds a fake that always throws <paramref name="exception"/>.</summary>
    public FakeLlmJudgeClient(Exception exception)
    {
        cannedResponse = null;
        throwException = exception;
    }

    /// <summary>Number of times <see cref="CompleteAsync"/> has been invoked (read-only).</summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        CallCount++;

        return throwException is not null
            ? throw throwException
            : Task.FromResult(cannedResponse ?? string.Empty);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
