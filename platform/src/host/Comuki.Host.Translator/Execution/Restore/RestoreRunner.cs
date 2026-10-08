using Comuki.Shared.Contracts.Environments;

namespace Comuki.Host.Translator.Execution.Restore;

/// <summary>
/// Result of one work-item restore step: a successful run, a class/toml
/// mismatch (the orchestrator stamped a different class than the toml
/// declares — that is a "fail the item" signal, not a translate signal),
/// a missing toml (no restore to run, the loop proceeds), an invalid
/// toml (closed-schema or unsupported runtime — fail the item), an
/// opcode-execution failure (non-zero exit, launch failure, or timeout
/// — fail the item). The distinction matters for the loop's decision
/// to fail with a stable reason.
/// </summary>
public enum RestoreOutcomeKind
{
    /// <summary>Restore ran (or there was nothing to run); proceed to pi.</summary>
    Succeeded,

    /// <summary>The toml is missing — restore is a no-op for this slot.</summary>
    Skipped,

    /// <summary>The toml's <c>class</c> disagrees with the worker's claimed class — class-mismatched claim.</summary>
    ClassMismatch,

    /// <summary>The toml failed to parse (closed-schema rejection, syntax, etc.).</summary>
    InvalidToml,

    /// <summary>An opcode exited non-zero, failed to launch, or hit the timeout.</summary>
    OpcodeFailed,
}

/// <summary>
/// Restore step outcome plus the human-readable reason the loop forwards
/// to <c>POST /workers/{id}/fail</c> when the kind is a failure kind.
/// </summary>
/// <param name="Kind">How the step ended.</param>
/// <param name="Reason">Single-line failure reason; empty on success/skipped.</param>
public sealed record RestoreOutcome(RestoreOutcomeKind Kind, string Reason);

/// <summary>
/// After-clone restore runner (add-worker-environments 4.3, spec scenario
/// "Restore opcodes run on the slot after clone"). Reads the cloned
/// repository's <c>.comuki/environment.toml</c>, validates the declared
/// class against the worker's claimed class (a class-mismatched claim is
/// a fail-the-item signal — the compute provider should not have
/// dispatched this worker), and runs each opcode in catalog order via
/// <see cref="IRestoreProcessRunner"/>. A non-zero exit, a launch
/// failure, or a timeout fails the step before pi starts.
/// <para>
/// The runner owns the file-system surface and the catalog-order loop;
/// the runner-side <see cref="RestoreProcessRunner"/> owns the
/// <c>Process.Start</c> plumbing. Tests inject a fake
/// <see cref="IRestoreProcessRunner"/> to assert which opcode was spawned
/// with what arguments — the real runner uses the same house shape as
/// <c>GenericCommandProcessRunner</c>.
/// </para>
/// <para>
/// <b>Why the catalog-order loop.</b> The bound class advertises a set of
/// opcodes (e.g. <c>dotnet</c>, <c>bun</c>) — when multiple are declared
/// in <c>[restore]</c>, running them in catalog order keeps the host
/// deterministic across worker restarts (a cold volume, a partial cache).
/// Iteration order is the catalog's declared order, not the toml's text
/// order, so a repo can re-order its own toml without changing the
/// restore sequence.
/// </para>
/// </summary>
/// <param name="processRunner">Process seam — a fake in tests.</param>
/// <param name="logger">Structured logger.</param>
public sealed class RestoreRunner(
    IRestoreProcessRunner processRunner,
    ILogger<RestoreRunner> logger)
{
    /// <summary>
    /// Path the parser reads; constant so a hostile repo cannot smuggle a
    /// <c>..</c> path through configuration.
    /// </summary>
    public const string EnvironmentTomlRelativePath = ".comuki/environment.toml";

    /// <summary>
    /// Per-argument timeout for an opcode invocation. Restore is the
    /// hot path of a cold worker slot — 5 minutes is the upper bound a
    /// <c>dotnet restore</c> of a mid-size solution should ever need,
    /// and <c>bun install</c> on a small monorepo lands well under.
    /// </summary>
    public static readonly TimeSpan OpcodeTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads <see cref="EnvironmentTomlRelativePath"/> under
    /// <paramref name="workingDirectory"/>, validates the class, and
    /// runs each declared opcode. The returned kind tells the loop
    /// whether to proceed with pi (<see cref="RestoreOutcomeKind.Succeeded"/>
    /// / <see cref="RestoreOutcomeKind.Skipped"/>) or fail the item
    /// (the three failure kinds).
    /// </summary>
    /// <param name="workingDirectory">The cloned repository root.</param>
    /// <param name="workerEnvClass">
    /// The environment class the worker was scaled for (the same class the
    /// compute provider stamped on the container as <c>COMUKI_ENV_CLASS</c>).
    /// </param>
    /// <param name="allowedOpcodes">
    /// The opcodes the bound class advertises; every key in
    /// <see cref="EnvironmentTomlFile.Restore"/> must be a member. The
    /// Translator already knows this set from the same catalog the compute
    /// provider reads — passing it in keeps the runner catalog-agnostic.
    /// </param>
    /// <param name="cancellationToken">Cancels the step mid-restore.</param>
    public async Task<RestoreOutcome> RunAsync(
        string workingDirectory,
        string workerEnvClass,
        IReadOnlySet<string> allowedOpcodes,
        CancellationToken cancellationToken = default)
    {
        var tomlPath = Path.Combine(workingDirectory, EnvironmentTomlRelativePath);
        if (!File.Exists(tomlPath))
        {
            logger.LogDebug(
                "No {RelativePath} under {WorkingDirectory} — restore is a no-op for this slot",
                EnvironmentTomlRelativePath,
                workingDirectory);
            return new RestoreOutcome(RestoreOutcomeKind.Skipped, string.Empty);
        }

        var content = await File.ReadAllTextAsync(tomlPath, cancellationToken);
        if (!EnvironmentToml.TryParse(content, out var file, out var parseErrors))
        {
            var reason = string.Join("; ", parseErrors);
            logger.LogWarning(
                "Refusing to run restore: {RelativePath} is invalid ({Reason})",
                EnvironmentTomlRelativePath,
                reason);
            return new RestoreOutcome(RestoreOutcomeKind.InvalidToml, reason);
        }

        if (!string.Equals(file.Class, workerEnvClass, StringComparison.Ordinal))
        {
            var reason = $"environment.toml class '{file.Class}' disagrees with worker env class '{workerEnvClass}' — class-mismatched claim.";
            logger.LogWarning(
                "Refusing to run restore: {Reason}",
                reason);
            return new RestoreOutcome(RestoreOutcomeKind.ClassMismatch, reason);
        }

        if (!EnvironmentToml.ValidateRestoreOpcodes(file, allowedOpcodes, out var opcodeErrors))
        {
            var reason = string.Join("; ", opcodeErrors);
            logger.LogWarning(
                "Refusing to run restore: environment.toml opcodes are not in the bound class ({Reason})",
                reason);
            return new RestoreOutcome(RestoreOutcomeKind.InvalidToml, reason);
        }

        if (file.Restore.Count == 0)
        {
            logger.LogInformation(
                "No restore opcodes declared in {RelativePath} — restore is a no-op for this slot",
                EnvironmentTomlRelativePath);
            return new RestoreOutcome(RestoreOutcomeKind.Succeeded, string.Empty);
        }

        // Iterate in catalog order, not toml text order, so the restore
        // sequence is host-deterministic across restarts.
        foreach (var opcode in allowedOpcodes)
        {
            if (!file.Restore.TryGetValue(opcode, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                var invocation = BuildInvocation(opcode, target, workingDirectory);
                logger.LogInformation(
                    "Running restore opcode {Opcode} for target {Target} in {WorkingDirectory}",
                    opcode,
                    target,
                    invocation.WorkingDirectory ?? workingDirectory);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(OpcodeTimeout);

                var result = await processRunner.RunAsync(
                    invocation.Executable,
                    invocation.Arguments,
                    invocation.WorkingDirectory,
                    timeoutCts.Token);

                if (result.ExitCode is not 0)
                {
                    var reason = result.LaunchFailureDetail is { } detail
                        ? $"restore opcode '{opcode}' for '{target}' failed to launch: {detail}"
                        : $"restore opcode '{opcode}' for '{target}' exited {result.ExitCode}";
                    logger.LogWarning("Restore opcode failed: {Reason}", reason);
                    return new RestoreOutcome(RestoreOutcomeKind.OpcodeFailed, reason);
                }
            }
        }

        return new RestoreOutcome(RestoreOutcomeKind.Succeeded, string.Empty);
    }

    /// <summary>
    /// Translates one (opcode, target) pair to a process invocation. The
    /// shape is fixed by the spec — only the two golden opcodes ship in
    /// the v1 catalog; extending it is a catalog-level decision.
    /// </summary>
    internal static RestoreInvocation BuildInvocation(string opcode, string target, string workingDirectory)
    {
        return opcode switch
        {
            "dotnet" => new RestoreInvocation(
                Executable: "dotnet",
                Arguments: ["restore", target],
                WorkingDirectory: workingDirectory),
            "bun" => new RestoreInvocation(
                Executable: "bun",
                Arguments: ["install"],
                WorkingDirectory: Path.Combine(workingDirectory, target)),
            _ => throw new InvalidOperationException(
                $"unknown restore opcode '{opcode}' — the catalog filter should have rejected it before this point."),
        };
    }

    /// <summary>One translated restore invocation: executable + arguments + cwd.</summary>
    internal sealed record RestoreInvocation(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory);
}
