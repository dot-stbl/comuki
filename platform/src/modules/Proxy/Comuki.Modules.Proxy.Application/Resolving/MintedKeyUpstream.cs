using Comuki.Modules.Proxy.Application.Models;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// Picks the upstream a runtime mint inherits (issue #122): the project's
/// own configured key first, else the <c>anthropic</c> provider key (the
/// worker runtime speaks Anthropic-shaped traffic), else the
/// ordinal-first provider so the pick is deterministic. Pure helper —
/// no private methods on the store (<c>code-shape.md</c> §1a).
/// </summary>
internal static class MintedKeyUpstream
{
    /// <summary>Provider the worker runtime's traffic is shaped for; preferred when the project has no key of its own.</summary>
    public const string AnthropicProvider = "anthropic";

    /// <summary>Returns the upstream for a minted key, or throws when no configured key carries one.</summary>
    /// <param name="configured">Config-seeded keys from the store snapshot.</param>
    /// <param name="projectId">Project the mint is bound to.</param>
    public static UpstreamSpec Select(IEnumerable<VirtualKey> configured, ProjectId projectId)
    {
        if (configured.FirstOrDefault(key => key.ProjectId == projectId) is { } projectKey)
        {
            return projectKey.Upstream;
        }

        var candidates = configured
            .OrderBy(key => string.Equals(key.Upstream.Provider, AnthropicProvider, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(key => key.Upstream.Provider, StringComparer.Ordinal)
            .ToList();

        return candidates.Count > 0
            ? candidates[0].Upstream
            : throw new InvalidOperationException(
                "cannot mint a virtual key: Proxy:VirtualKeys carries no upstream to inherit");
    }
}
