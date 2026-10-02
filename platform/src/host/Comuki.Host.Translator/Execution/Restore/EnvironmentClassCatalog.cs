namespace Comuki.Host.Translator.Execution.Restore;

/// <summary>
/// Class-to-opcodes map for the v1 catalog, hardcoded on the Translator
/// side. Mirrors <c>DefaultEnvironmentCatalog</c> in
/// <c>Comuki.Engine.Compute</c>; the Translator cannot reference the
/// Compute assembly (a host module that lives one layer over the worker
/// would create a backwards dependency). Keeping a tiny parallel map
/// here is the seam the spec relies on: the orchestrator-stamped class
/// matches a host-known set, and the toml's restore keys must be a
/// subset of that set.
/// <para>
/// Extending the v1 catalog (e.g. <c>cpp-clang-18-linux</c>) is a
/// catalog-level change in both Compute and the Translator — both maps
/// move together; the spec mandates neither is free to freelance an
/// opcode set.
/// </para>
/// </summary>
internal static class EnvironmentClassCatalog
{
    /// <summary>The v1 opcodes the bound class advertises; the empty set means no restore opcodes are permitted.</summary>
    /// <param name="envClass">The class the worker was scaled for.</param>
    public static IReadOnlySet<string> OpcodesFor(string envClass)
    {
        // The keys match DefaultEnvironmentCatalog.Net10SdkId /
        // DefaultEnvironmentCatalog.Net10SdkBunId (string-comparison
        // ordinal — the catalog is too). Unknown classes get an empty
        // set, which makes the toml's restore keys fail validation
        // rather than let a class-mismatched claim smuggle in opcodes.
        return envClass switch
        {
            "net10-sdk" => new HashSet<string>(StringComparer.Ordinal) { "dotnet" },
            "net10-sdk-bun" => new HashSet<string>(StringComparer.Ordinal) { "dotnet", "bun" },
            _ => new HashSet<string>(StringComparer.Ordinal),
        };
    }
}
