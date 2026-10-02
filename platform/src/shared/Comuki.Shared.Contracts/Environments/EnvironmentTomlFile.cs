namespace Comuki.Shared.Contracts.Environments;

/// <summary>
/// Parsed <c>.comuki/environment.toml</c> projection (add-worker-environments
/// 4.1, spec scenario "Valid Comuki dogfood file is accepted"). Both the
/// Host (work-item attach) and the Translator (after-clone restore) read this
/// shape; the parser (<see cref="EnvironmentToml" />) is the only producer.
/// </summary>
/// <param name="Class">Catalog bundle id the work item binds to
///     (<c>net10-sdk-bun</c>, <c>net10-sdk</c>, <c>cpp-clang-18-linux</c>).
///     Non-empty, present in the fleet catalog at validation time — the parser
///     only checks non-emptiness, the class-aware validation lives in the
///     Engine.Compute bundle catalog.</param>
/// <param name="Runtime">Worker runtime. One of <c>linux</c> | <c>windows</c>.</param>
/// <param name="Restore">Opcodes the bound class permits — keys are opcodes
///     (<c>dotnet</c>, <c>bun</c>, …) and values are the targets each opcode
///     consumes (a single scalar becomes a one-element list, so the restore
///     runner can iterate without branching on the wire shape).</param>
/// <param name="Mounts">Mounts the class advertises; key is the mount id, value
///     is the host-side source. Validated against the class's mount manifest
///     by the same path that validates <c>restore</c>.</param>
/// <param name="Verify">Optional table of verify-time opcodes (isolate-verifier-runtime
///     1.1, spec scenario "Verify uses the item's class"). Keys are opcodes the bound
///     class advertises (the same set as <c>Restore</c>); values are positional targets
///     the Translator's <c>VerifyRunner</c> iterates after restore and before the
///     work item is reported complete. The table is optional — a repo with no
///     <c>[verify]</c> table skips the verify step (no behaviour change for
///     existing dogfood files).</param>
public sealed record EnvironmentTomlFile(
    string Class,
    string Runtime,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Restore,
    IReadOnlyDictionary<string, string> Mounts,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Verify = null);
