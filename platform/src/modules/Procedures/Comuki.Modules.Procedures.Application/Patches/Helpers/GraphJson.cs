using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Patches.Diff;

namespace Comuki.Modules.Procedures.Application.Patches.Helpers;

/// <summary>
/// Deserialization of the compiled graph stored in a pinned version —
/// shared by the diff and publication flows. Extracted per the
/// no-private-methods rule.
/// </summary>
internal static class GraphJson
{
    /// <summary>
    /// Deserializes a stored GraphJson into a
    /// <see cref="ProcedureGraph"/>; a null result is a corrupt stored
    /// version and a loud refusal.
    /// </summary>
    /// <param name="graphJson">The stored graph JSON.</param>
    /// <returns>The deserialized graph.</returns>
    public static ProcedureGraph Deserialize(string graphJson)
    {
        return System.Text.Json.JsonSerializer.Deserialize<ProcedureGraph>(graphJson, System.Text.Json.JsonSerializerOptions.Web)
            ?? throw new GraphPatchException(
                GraphPatchException.InvalidBaseVersion,
                "Compiled version's graph JSON deserialized to null; the stored version is corrupt.");
    }
}
