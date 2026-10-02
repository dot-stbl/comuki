using Comuki.Modules.Procedures.Domain.Definitions;

namespace Comuki.Modules.Procedures.Application.Compiler.Model;

/// <summary>
/// The authored procedure artifact (design decision 1: lives in the
/// client's git, compiled by the platform). This is the input shape the
/// compile gate reads: the project identity, the graph the layering merge
/// (task 2.2) produced, and the git provenance.
/// </summary>
/// <param name="ProjectId">The project the procedure belongs to.</param>
/// <param name="ProcedureKey">Stable key the project uses to reference the procedure.</param>
/// <param name="GitRef">The git ref the definition was loaded from — provenance, not identity.</param>
/// <param name="Graph">The layered procedure graph (nodes + edges) the compile gate validates.</param>
public sealed record ProcedureDefinition(
    Guid ProjectId,
    string ProcedureKey,
    string GitRef,
    ProcedureGraph Graph);
