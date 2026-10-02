using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Diff;

namespace Comuki.Modules.Procedures.Application.Patches.Chat;

/// <summary>The chat-facing result: a draft patch with its rendered diff.</summary>
/// <param name="Patch">The draft GraphPatch — not published, not publishable from chat.</param>
/// <param name="Diff">The semantic diff Studio renders.</param>
public sealed record ProposedPatch(
    GraphPatch Patch,
    GraphPatchDiff Diff);
