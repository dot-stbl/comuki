using Comuki.Modules.Verify.Domain.Exceptions;
using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Verify.Domain.Runs;

/// <summary>
/// One row in the generic-command verification table. The worker polls
/// <see cref="GenericCommandStatus.Pending"/> rows on a fixed interval,
/// launches <see cref="Executable"/> with <see cref="Arguments"/> through
/// <c>Process.Start</c> and stamps the lifecycle:
/// <list type="bullet">
///   <item><see cref="GenericCommandStatus.Pending"/> → <see cref="GenericCommandStatus.Running"/>: stamps <see cref="StartedAt"/>.</item>
///   <item><see cref="GenericCommandStatus.Running"/> → <see cref="GenericCommandStatus.Green"/> when the exit code matches <see cref="ExpectedExitCode"/>.</item>
///   <item><see cref="GenericCommandStatus.Running"/> → <see cref="GenericCommandStatus.Red"/> otherwise (and on non-zero / non-matching exit, or a launch failure).</item>
/// </list>
/// <para>
/// <see cref="Executable"/> + <see cref="Arguments"/> are kept as an
/// executable name and a separate argument array — never a single
/// concatenated command line — so the runner can hand them straight to
/// <c>ProcessStartInfo.ArgumentList</c> with <c>UseShellExecute = false</c>
/// and no shell ever parses operator-supplied text (see
/// <c>GenericCommandProcessRunner</c>). <see cref="ProfileKey"/> mirrors
/// the rest of the platform — operators route the run through a
/// control-plane profile so the same runner pipeline (claim / pin /
/// execute) can target a dedicated image when the v1.1 fleet
/// runner-container lands (GH issue #47).
/// </para>
/// <para>
/// <b>Isolation warning:</b> until GH issue #47 ships, the runner
/// executes <see cref="Executable"/> directly inside the orchestrator
/// host process — there is no container/sandbox boundary yet. The
/// verifier is disabled by default (<c>Verify:Verifier:Enabled</c>) for
/// exactly this reason; enable it only where every possible
/// <see cref="Executable"/> value is already trusted.
/// </para>
/// <para>
/// <see cref="OutputLog"/> captures stdout+stderr in order, with a soft
/// cap enforced by the runner (truncation is logged but does not flip
/// the verdict). <see cref="ProjectId"/> is nullable because the global
/// gate runs are not project-scoped — the scheduler dispatcher always
/// supplies a project, the operator-triggered gate can leave it null.
/// </para>
/// </summary>
public sealed class GenericCommandRun
{
    internal GenericCommandRun()
    {
    }

    /// <summary>Strong-typed run id.</summary>
    public GenericCommandRunId Id { get; private set; }

    /// <summary>Optional owning project (null for global gate runs).</summary>
    public ProjectId? ProjectId { get; private set; }

    /// <summary>
    /// Profile key the worker should resolve through the control plane.
    /// Empty when the run bypasses the profile system (smoke tests,
    /// operator one-offs).
    /// </summary>
    public string ProfileKey { get; private set; } = string.Empty;

    /// <summary>The executable the runner hands to <c>Process.Start</c> — never a shell.</summary>
    public string Executable { get; private set; } = string.Empty;

    /// <summary>
    /// Positional arguments for <see cref="Executable"/>, in order.
    /// Passed to <c>ProcessStartInfo.ArgumentList</c> verbatim — no
    /// string concatenation, no shell re-parsing, so an argument
    /// containing spaces or shell metacharacters stays exactly one
    /// argument on the child's <c>argv</c>.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; private set; } = [];

    /// <summary>
    /// Exit code that maps to <see cref="GenericCommandStatus.Green"/>.
    /// Defaults to 0 — most verification gates want exactly that.
    /// </summary>
    public int ExpectedExitCode { get; private set; }

    /// <summary>Current lifecycle status.</summary>
    public GenericCommandStatus Status { get; private set; }

    /// <summary>When the row was inserted.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the worker started the process; null while pending.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When the worker recorded a terminal status; null while pending/running.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Actual exit code reported by the process; null while pending/running or on a launch failure.</summary>
    public int? ActualExitCode { get; private set; }

    /// <summary>Captured stdout+stderr (line-ordered), truncated by the runner to a soft cap.</summary>
    public string OutputLog { get; private set; } = string.Empty;

    /// <summary>
    /// Creates a pending run. The caller supplies
    /// <paramref name="expectedExitCode"/> because the gate semantic is
    /// "exit code == expected" (most callers want 0).
    /// </summary>
    /// <param name="projectId">Optional owning project.</param>
    /// <param name="profileKey">Empty bypasses profile resolution.</param>
    /// <param name="executable">The executable for <c>Process.Start</c> — never a shell command line.</param>
    /// <param name="arguments">Positional arguments, passed verbatim to <c>ArgumentList</c>.</param>
    /// <param name="expectedExitCode">The code that maps to Green (default 0).</param>
    /// <param name="now">Wall-clock source.</param>
    /// <exception cref="ArgumentException"><paramref name="executable"/> is blank.</exception>
    public static GenericCommandRun Create(
        ProjectId? projectId,
        string profileKey,
        string executable,
        IReadOnlyList<string> arguments,
        int expectedExitCode,
        DateTimeOffset now)
    {
        var trimmedExecutable = (executable ?? string.Empty).Trim();
        // The project rule bans ArgumentException.ThrowIf*; the explicit
        // if-throw is the canonical validation shape (matches the
        // pattern in MinioRunArtifactStore.BuildObjectKey).
#pragma warning disable IDE0046
        if (trimmedExecutable.Length == 0)
        {
            throw new ArgumentException("executable must not be empty", nameof(executable));
        }
#pragma warning restore IDE0046

        return new GenericCommandRun
        {
            Id = GenericCommandRunId.New(),
            ProjectId = projectId,
            ProfileKey = (profileKey ?? string.Empty).Trim(),
            Executable = trimmedExecutable,
            Arguments = arguments ?? [],
            ExpectedExitCode = expectedExitCode,
            Status = GenericCommandStatus.Pending,
            StartedAt = null,
            FinishedAt = null,
            ActualExitCode = null,
            OutputLog = string.Empty,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Marks the run as in-flight: stamps <see cref="StartedAt"/> and
    /// rejects anything but a <see cref="GenericCommandStatus.Pending"/>
    /// source. The store's <c>FOR UPDATE SKIP LOCKED</c> query guards
    /// against two workers claiming the same row.
    /// </summary>
    /// <param name="now">Wall-clock instant to stamp as <see cref="StartedAt"/>.</param>
    /// <exception cref="IllegalGenericCommandStatusTransitionException">Status is not Pending.</exception>
    public void MarkRunning(DateTimeOffset now)
    {
        if (Status != GenericCommandStatus.Pending)
        {
            throw new IllegalGenericCommandStatusTransitionException(Status, GenericCommandStatus.Running);
        }

        Status = GenericCommandStatus.Running;
        StartedAt = now;
    }

    /// <summary>
    /// Terminal transition: writes the actual exit code, the captured
    /// log, and the verdict (Green when <see cref="ActualExitCode"/>
    /// equals <see cref="ExpectedExitCode"/>, Red otherwise). Legal only
    /// from <see cref="GenericCommandStatus.Running"/>.
    /// </summary>
    /// <param name="actualExitCode">Process exit code reported by <c>Process.ExitCode</c>.</param>
    /// <param name="outputLog">Captured stdout+stderr (may be truncated by the runner).</param>
    /// <param name="now">Wall-clock instant to stamp as <see cref="FinishedAt"/>.</param>
    /// <exception cref="IllegalGenericCommandStatusTransitionException">Status is not Running.</exception>
    public void MarkCompleted(int actualExitCode, string outputLog, DateTimeOffset now)
    {
        if (Status != GenericCommandStatus.Running)
        {
            throw new IllegalGenericCommandStatusTransitionException(Status, GenericCommandStatus.Green);
        }

        ActualExitCode = actualExitCode;
        OutputLog = outputLog ?? string.Empty;
        Status = actualExitCode == ExpectedExitCode
            ? GenericCommandStatus.Green
            : GenericCommandStatus.Red;
        FinishedAt = now;
    }

    /// <summary>
    /// Failure path: the runner could not even launch the process (file
    /// not found, access denied, timeout before exit). Marked Red with
    /// an explanatory message in <see cref="OutputLog"/>.
    /// </summary>
    /// <param name="errorDetail">Short failure reason — logged into <see cref="OutputLog"/>.</param>
    /// <param name="now">Wall-clock instant to stamp as <see cref="FinishedAt"/>.</param>
    /// <exception cref="IllegalGenericCommandStatusTransitionException">Status is not Running.</exception>
    public void MarkLaunchFailed(string errorDetail, DateTimeOffset now)
    {
        if (Status != GenericCommandStatus.Running)
        {
            throw new IllegalGenericCommandStatusTransitionException(Status, GenericCommandStatus.Red);
        }

        Status = GenericCommandStatus.Red;
        ActualExitCode = null;
        OutputLog = errorDetail ?? string.Empty;
        FinishedAt = now;
    }
}
