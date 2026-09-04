using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Proxy.Application.Models;

/// <summary>
/// One meterable proxy call: the tokens + cost the response reported,
/// the model the upstream actually ran, and the project attribution. The
/// meter passes it to <c>IUsageRecorder</c> in the costs module so the
/// sum-rollup and budget gate fire on the same row the brain / worker
/// reporters produce.
/// </summary>
/// <param name="ProjectId">Project the spend belongs to.</param>
/// <param name="Model">Model id the upstream reported (after <c>model</c> normalisation).</param>
/// <param name="InputTokens">Prompt / input tokens consumed.</param>
/// <param name="OutputTokens">Completion / output tokens consumed.</param>
/// <param name="CostUsdMicros">Cost computed from the per-model price table; 1 USD = 1_000_000.</param>
/// <param name="OccurredAt">Wall-clock when the upstream finished the call.</param>
public sealed record ProxyUsageReport(
    ProjectId ProjectId,
    string Model,
    int InputTokens,
    int OutputTokens,
    long CostUsdMicros,
    DateTimeOffset OccurredAt);
