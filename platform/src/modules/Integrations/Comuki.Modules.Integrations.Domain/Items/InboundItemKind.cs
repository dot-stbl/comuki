namespace Comuki.Modules.Integrations.Domain.Items;

/// <summary>
/// What kind of tracker-side object this inbound item represents: a plain
/// issue or a pull request / merge request. The webhook mapper picks
/// the value; the profile router reads it to choose between
/// <c>general</c> (issues) and <c>pr-review</c> (PRs).
/// </summary>
public enum InboundItemKind
{
    /// <summary>A regular tracker issue (the default).</summary>
    Issue,

    /// <summary>A pull request / merge request (inbound review surface).</summary>
    PullRequest,
}
