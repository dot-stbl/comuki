namespace Comuki.Host.Brain.Ports;

/// <summary>One active run as the brain sees it.</summary>
/// <param name="RunId">Run id string.</param>
/// <param name="ProjectSlug">Project the run belongs to.</param>
/// <param name="Status">Run status key.</param>
/// <param name="StartedAt">When the run started.</param>
public sealed record ActiveRunView(
    string RunId,
    string ProjectSlug,
    string Status,
    DateTimeOffset StartedAt);
