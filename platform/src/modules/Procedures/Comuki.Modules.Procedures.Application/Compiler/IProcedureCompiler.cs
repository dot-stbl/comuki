using Comuki.Modules.Procedures.Application.Compiler.Model;
namespace Comuki.Modules.Procedures.Application.Compiler;

/// <summary>
/// Compile-gate port: takes a procedure definition (the authored artifact
/// from the client's git, per design decision 1) and returns a compiled,
/// content-addressed version. Implementations live in the Infrastructure
/// layer (task 2.3) — the compile gate composes the catalog validator
/// (Domain), the editions gate (Domain), the versioned store (this
/// project), and the structural compile checks. The Application port
/// keeps the host composition free of compile-gate internals.
/// </summary>
/// <remarks>
/// Compilation is pure: identical inputs produce an identical
/// content-addressed version id (spec requirement "Deterministic compile
/// gate"). Refusal is loud and typed — the gate emits
/// <see cref="Domain.Exceptions.ProcedureNodeKindsDomainException"/>
/// (or a sibling typed exception) naming the offending node and the rule.
/// </remarks>
public interface IProcedureCompiler
{
    /// <summary>
    /// Compiles <paramref name="definition"/> into an immutable
    /// content-addressed version. The host calls this when an operator
    /// requests publication; the orchestration layer calls it during
    /// the procedure-pinned materialization path (task 4.2).
    /// </summary>
    /// <param name="definition">The authored procedure definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CompiledProcedureVersion> CompileAsync(
        ProcedureDefinition definition,
        CancellationToken cancellationToken = default);
}
