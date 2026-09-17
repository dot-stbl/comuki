namespace Comuki.Modules.Memory.Application.Learning;

/// <summary>
/// One worker (or future signal-source) proposal for the learning loop —
/// the payload of the MCP <c>learning.suggest</c> tool. A suggestion with
/// the same (project, topic, rule) as a pending candidate bumps that
/// candidate's repeat counter instead of queueing a duplicate row.
/// </summary>
/// <param name="ProjectId">The project whose workers suggested the rule — the candidate's scope.</param>
/// <param name="Topic">Short topic key (e.g. <c>build.dotnet</c>).</param>
/// <param name="Observation">What the worker observed — the evidence.</param>
/// <param name="ProposedRule">The candidate rule text, stated as an imperative.</param>
/// <param name="SourceRef">Where the signal came from (e.g. <c>worker:{id}</c>).</param>
public sealed record LearningSuggestion(
    Guid ProjectId,
    string Topic,
    string Observation,
    string ProposedRule,
    string SourceRef);
