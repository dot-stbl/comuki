using Comuki.Host.Translator.Execution.Restore;
using Comuki.Shared.Contracts.Environments;

namespace Comuki.Host.Translator.Execution.Verify;

/// <summary>
/// After-clone verify runner (isolate-verifier-runtime 1.1, spec
/// scenario "Verify uses the item's class" / "Verify may skip the
/// coding agent"). Reads the cloned repository's
/// <c>.comuki/environment.toml</c>, validates the declared class against
/// the worker's claimed class (a class-mismatched claim is a
/// fail-the-item signal — the compute provider should not have
/// dispatched this worker), and runs each declared opcode in catalog
/// order via <see cref="IRestoreProcessRunner"/>. The verify path is
/// the post-restore counterpart of <see cref="RestoreRunner"/>: same
/// shape, same seam, same house rules — but no class needs to be added
/// when the toml's <c>[verify]</c> table is empty.
/// <para>
/// <b>Why the catalog-order loop.</b> The bound class advertises a set
/// of opcodes (e.g. <c>dotnet</c>, <c>bun</c>) — when multiple are
/// declared in <c>[verify]</c>, running them in catalog order keeps
/// the host deterministic across worker restarts (a cold volume, a
/// partial cache). Iteration order is the catalog's declared order, not
/// the toml's text order, so a repo can re-order its own toml without
/// changing the verify sequence.
/// </para>
/// <para>
/// <b>Why a verify runner at all.</b> The spec scenario
/// "Verify may skip the coding agent" mandates that a verify execution
/// run restore (already done by the loop) plus class-advertised verify
/// opcodes — and SHALL NOT be required to spawn pi. Failing the verify
/// step is a <c>verify.failed</c> journal entry plus a <c>fail</c>
/// REST call on the work item; the Host stays up.
/// </para>
/// </summary>
/// <param name="processRunner">Process seam — same shape the restore runner uses; a fake in tests.</param>
/// <param name="logger">Structured logger.</param>
public sealed class VerifyRunner(
    IRestoreProcessRunner processRunner,
    ILogger<VerifyRunner> logger)
{
    /// <summary>
    /// Path the parser reads; constant so a hostile repo cannot smuggle a
    /// <c>..</c> path through configuration. Same constant the restore
    /// runner uses — the verify step runs against the same file the
    /// restore step already parsed.
    /// </summary>
    public const string EnvironmentTomlRelativePath = RestoreRunner.EnvironmentTomlRelativePath;

    /// <summary>
    /// Per-argument timeout for a verify opcode invocation. Verify is
    /// the hot path of a warm worker slot — 10 minutes is the upper bound
    /// a <c>dotnet build</c> of a mid-size solution should ever need,
    /// and <c>dotnet run --project &lt;tests&gt;</c> of one suite lands
    /// well under. The restore runner uses 5 minutes (cold cache);
    /// verify uses 10 because the cache is already warm.
    /// </summary>
    public static readonly TimeSpan OpcodeTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Reads <see cref="EnvironmentTomlRelativePath"/> under
    /// <paramref name="workingDirectory"/>, validates the class, and
    /// runs each declared opcode. The returned kind tells the loop
    /// whether to report <c>verify.completed</c> + complete the item
    /// (<see cref="VerifyOutcomeKind.Succeeded"/> /
    /// <see cref="VerifyOutcomeKind.Skipped"/>) or report
    /// <c>verify.failed</c> + fail the item (the three failure kinds).
    /// </summary>
    /// <param name="workingDirectory">The cloned repository root.</param>
    /// <param name="workerEnvClass">
    /// The environment class the worker was scaled for (the same class the
    /// compute provider stamped on the container as <c>COMUKI_ENV_CLASS</c>).
    /// </param>
    /// <param name="allowedOpcodes">
    /// The opcodes the bound class advertises; every key in
    /// <see cref="EnvironmentTomlFile.Verify"/> must be a member. The
    /// Translator already knows this set from the same catalog the compute
    /// provider reads — passing it in keeps the runner catalog-agnostic.
    /// </param>
    /// <param name="cancellationToken">Cancels the step mid-verify.</param>
    public async Task<VerifyOutcome> RunAsync(
        string workingDirectory,
        string workerEnvClass,
        IReadOnlySet<string> allowedOpcodes,
        CancellationToken cancellationToken = default)
    {
        var tomlPath = Path.Combine(workingDirectory, EnvironmentTomlRelativePath);
        if (!File.Exists(tomlPath))
        {
            logger.LogDebug(
                "No {RelativePath} under {WorkingDirectory} — verify is a no-op for this slot",
                EnvironmentTomlRelativePath,
                workingDirectory);
            return new VerifyOutcome(VerifyOutcomeKind.Skipped, string.Empty, [], string.Empty, null);
        }

        var content = await File.ReadAllTextAsync(tomlPath, cancellationToken);
        if (!EnvironmentToml.TryParse(content, out var file, out var parseErrors))
        {
            var reason = string.Join("; ", parseErrors);
            logger.LogWarning(
                "Refusing to run verify: {RelativePath} is invalid ({Reason})",
                EnvironmentTomlRelativePath,
                reason);
            return new VerifyOutcome(VerifyOutcomeKind.InvalidToml, reason, [], string.Empty, null);
        }

        if (!string.Equals(file.Class, workerEnvClass, StringComparison.Ordinal))
        {
            var reason = $"environment.toml class '{file.Class}' disagrees with worker env class '{workerEnvClass}' — class-mismatched claim.";
            logger.LogWarning(
                "Refusing to run verify: {Reason}",
                reason);
            return new VerifyOutcome(VerifyOutcomeKind.ClassMismatch, reason, [], string.Empty, null);
        }

        var verifyTable = file.Verify ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (verifyTable.Count == 0)
        {
            logger.LogInformation(
                "No verify opcodes declared in {RelativePath} — verify is a no-op for this slot",
                EnvironmentTomlRelativePath);
            return new VerifyOutcome(VerifyOutcomeKind.Skipped, string.Empty, [], string.Empty, null);
        }

        if (!EnvironmentToml.ValidateRestoreOpcodes(
                new EnvironmentTomlFile(
                    file.Class,
                    file.Runtime,
                    Restore: verifyTable,
                    Mounts: file.Mounts),
                allowedOpcodes,
                out var opcodeErrors))
        {
            var reason = string.Join("; ", opcodeErrors);
            logger.LogWarning(
                "Refusing to run verify: environment.toml opcodes are not in the bound class ({Reason})",
                reason);
            return new VerifyOutcome(VerifyOutcomeKind.InvalidToml, reason, [], string.Empty, null);
        }

        // Iterate in catalog order, not toml text order, so the verify
        // sequence is host-deterministic across restarts.
        var executed = new List<string>();
        foreach (var opcode in allowedOpcodes)
        {
            if (!verifyTable.TryGetValue(opcode, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                var invocation = BuildInvocation(opcode, target, workingDirectory);
                logger.LogInformation(
                    "Running verify opcode {Opcode} for target {Target} in {WorkingDirectory}",
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

                executed.Add($"{opcode}={target}");

                if (result.ExitCode is not 0)
                {
                    var reason = result.LaunchFailureDetail is { } detail
                        ? $"verify opcode '{opcode}' for '{target}' failed to launch: {detail}"
                        : $"verify opcode '{opcode}' for '{target}' exited {result.ExitCode}";
                    logger.LogWarning("Verify opcode failed: {Reason}", reason);
                    return new VerifyOutcome(
                        Kind: VerifyOutcomeKind.OpcodeFailed,
                        Reason: reason,
                        Opcodes: executed,
                        FailedOpcode: $"{opcode}={target}",
                        ExitCode: result.ExitCode);
                }
            }
        }

        return new VerifyOutcome(VerifyOutcomeKind.Succeeded, string.Empty, executed, string.Empty, null);
    }

    /// <summary>
    /// Translates one (opcode, target) pair to a process invocation. The
    /// shape is fixed by the spec — only the two golden opcodes ship in
    /// the v1 catalog; extending it is a catalog-level decision. The
    /// restore runner maps the same opcodes the same way; the only
    /// difference is the opcode-argument template (verify runs <c>build</c>
    /// and <c>run --project</c>; restore runs <c>restore</c> and
    /// <c>install</c>).
    /// </summary>
    private static VerifyInvocation BuildInvocation(string opcode, string target, string workingDirectory)
    {
        return opcode switch
        {
            "dotnet" => new VerifyInvocation(
                Executable: "dotnet",
                Arguments: BuildDotnetVerifyArguments(target),
                WorkingDirectory: workingDirectory),
            "bun" => new VerifyInvocation(
                Executable: "bun",
                Arguments: ["test", target],
                WorkingDirectory: Path.Combine(workingDirectory, target)),
            _ => throw new InvalidOperationException(
                $"unknown verify opcode '{opcode}' — the catalog filter should have rejected it before this point."),
        };
    }

    /// <summary>
    /// Maps the <c>[verify]</c> <c>dotnet</c> target to the right subcommand.
    /// A target that looks like a project path (ends in <c>.csproj</c>)
    /// runs <c>dotnet run --project &lt;target&gt;</c>; everything else is
    /// treated as a solution / project to <c>dotnet build</c>. Both are
    /// pure file-name checks against the toml text — the runner does not
    /// know what kind of repo it landed in.
    /// </summary>
    private static IReadOnlyList<string> BuildDotnetVerifyArguments(string target)
    {
        return target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? ["run", "--project", target]
            : ["build", target];
    }

    /// <summary>One translated verify invocation: executable + arguments + cwd.</summary>
    private sealed record VerifyInvocation(string Executable, IReadOnlyList<string> Arguments, string? WorkingDirectory);
}
