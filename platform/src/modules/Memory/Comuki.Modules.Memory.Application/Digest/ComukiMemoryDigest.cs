using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Shared.Contracts.Memory;

namespace Comuki.Modules.Memory.Application.Digest;

/// <summary>
/// Contract adapter over <see cref="MemoryDigest"/>: maps the wire-shaped
/// <see cref="MemoryDigestRequest"/> onto the module's scope model and
/// renders the typed digest result as the prompt-facing text the chat
/// graph's ThinkNode injects into the brain context (and journals). An
/// empty fact set renders as an empty string — the turn service's
/// "nothing fed, do not journal" signal.
/// </summary>
public sealed class ComukiMemoryDigest(MemoryDigest digest) : IMemoryDigest
{
    /// <inheritdoc />
    public async Task<string> BuildDigestAsync(MemoryDigestRequest request, CancellationToken cancellationToken = default)
    {
        var scope = request.ScopeKind switch
        {
            MemoryDigestScopes.User => MemoryScope.User,
            MemoryDigestScopes.Project => MemoryScope.Project,
            MemoryDigestScopes.Global => MemoryScope.Global,
            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.ScopeKind, "unknown memory digest scope kind"),
        };

        // global facts carry the well-known "global" subject instead of a
        // real owner id — the store canonicalizes whatever it gets, so the
        // exact casing here only has to match itself
        var subjectId = scope == MemoryScope.Global ? MemoryScopeKeys.GlobalSubject : request.SubjectId.ToString();

        var result = await digest.BuildAsync(request.Task, scope, subjectId, cancellationToken);
        return MemoryDigestText.Render(result);
    }
}

/// <summary>Renders the typed digest as the compact prompt text.</summary>
file static class MemoryDigestText
{
    public static string Render(MemoryDigestResult result)
    {
        return string.Join(
            "\n",
            [.. Section("Relevant memory", result.Relevant), .. Section("Standing memory", result.FreshestStanding)]);
    }

    private static IEnumerable<string> Section(string heading, IReadOnlyList<MemoryDigestEntry> entries)
    {
        return entries.Count == 0 ? [] : [heading, .. entries.Select(static entry => $"- [{entry.Kind}] {entry.TopicKey}: {entry.Text}")];
    }
}
