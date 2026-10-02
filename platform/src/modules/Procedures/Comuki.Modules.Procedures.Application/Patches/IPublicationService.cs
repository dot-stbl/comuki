using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Domain.Patches;
using Comuki.Modules.Procedures.Domain.Patches.Publication;
namespace Comuki.Modules.Procedures.Application.Patches;

/// <summary>
/// Application-layer seam the host calls to publish a procedure
/// version. The publication service orchestrates the
/// <see cref="GraphPatchValidator"/> (graph-shape consistency at
/// task 3.1), the <see cref="PublicationSurfaceValidator"/> (forbidden
/// surface at task 3.2), the compile gate (task 2.3), the version
/// store (task 2.4), and the outbox (this task) — one entry point
/// keeps the publication invariant in one place.
/// 
/// <para>
/// Publication is the only path that writes a new compiled version.
/// The brain drafts (<see cref="IGraphPatchDraftingService"/>);
/// a distinct human approves (this interface); a new immutable
/// version results; the outbox tells the rest of the platform.
/// </para>
/// </summary>
public interface IPublicationService
{
    /// <summary>
    /// Publishes a patched version: validates the patch, runs the
    /// forbidden-surface check, compiles, persists, and writes the
    /// <c>procedures.procedure.published.v1</c> outbox event. Returns
    /// the new immutable version; throws a typed
    /// <see cref="PublicationException"/> on any refusal.
    /// </summary>
    /// <param name="request">The publication request — the patch, the human approver, and the policy context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<Compiler.Model.CompiledProcedureVersion> PublishAsync(
        PublicationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input to <see cref="IPublicationService.PublishAsync"/>. The
/// request carries the patch, the human approver, the layered-merge
/// output the patch was drafted against, and the policy context the
/// forbidden-surface check reads.
/// </summary>
/// <param name="Patch">The durable proposal the brain (or operator) drafted.</param>
/// <param name="Approver">The human identity that approves the publication — must be distinct from the drafter.</param>
/// <param name="Context">The procedure's effective policy context (allowed kinds + autonomy/budget floors).</param>
/// <param name="LayeredProcedure">
/// The layered-procedure object the patch was drafted against
/// (carries the project's graph, the merged allowlist, and the
/// repository bindings). Required to project the patch back into
/// the compile gate's input shape.
/// </param>
public sealed record PublicationRequest(
    GraphPatch Patch,
    string Approver,
    PublicationContext Context,
    Domain.Layering.Results.LayeredProcedure LayeredProcedure);
