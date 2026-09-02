using Comuki.Modules.Costs.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Costs.Domain.Events;

/// <summary>
/// One metered usage row (tokens + money) attributed to a project and
/// optionally a run. Written by proxy / brain / worker reporters through
/// <c>IUsageRecorder</c>; read by <c>RunCostAggregator</c> and the costs API.
/// Money is stored as USD micros (1 USD = 1_000_000) to keep integer math.
/// </summary>
public sealed class UsageEvent
{
    internal UsageEvent()
    {
    }

    /// <summary>Event id (UUIDv7, client-side).</summary>
    public UsageEventId Id { get; private set; }

    /// <summary>Project the spend belongs to.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Optional run attribution; null for project-level / brain overhead.</summary>
    public RunId? RunId { get; private set; }

    /// <summary>Who reported the event.</summary>
    public UsageSource Source { get; private set; }

    /// <summary>Physical model id (provider-native), e.g. <c>claude-sonnet-4</c>.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>Prompt / input tokens.</summary>
    public int InputTokens { get; private set; }

    /// <summary>Completion / output tokens.</summary>
    public int OutputTokens { get; private set; }

    /// <summary>Cost in USD micros (1 USD = 1_000_000).</summary>
    public long CostUsdMicros { get; private set; }

    /// <summary>When the usage happened (reporter clock).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Creates a usage event; model is trimmed, tokens/cost must be non-negative.</summary>
    /// <param name="projectId"></param>
    /// <param name="runId"></param>
    /// <param name="source"></param>
    /// <param name="model"></param>
    /// <param name="inputTokens"></param>
    /// <param name="outputTokens"></param>
    /// <param name="costUsdMicros"></param>
    /// <param name="occurredAt"></param>
    public static UsageEvent Create(
        ProjectId projectId,
        RunId? runId,
        UsageSource source,
        string model,
        int inputTokens,
        int outputTokens,
        long costUsdMicros,
        DateTimeOffset occurredAt)
    {
        return string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("model must not be empty", nameof(model))
            : inputTokens < 0
            ? throw new ArgumentOutOfRangeException(nameof(inputTokens), inputTokens, "input tokens must be >= 0")
            : outputTokens < 0
            ? throw new ArgumentOutOfRangeException(nameof(outputTokens), outputTokens, "output tokens must be >= 0")
            : costUsdMicros < 0
            ? throw new ArgumentOutOfRangeException(nameof(costUsdMicros), costUsdMicros, "cost must be >= 0")
            : new UsageEvent
            {
                Id = UsageEventId.New(),
                ProjectId = projectId,
                RunId = runId,
                Source = source,
                Model = model.Trim(),
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostUsdMicros = costUsdMicros,
                OccurredAt = occurredAt,
            };
    }
}
