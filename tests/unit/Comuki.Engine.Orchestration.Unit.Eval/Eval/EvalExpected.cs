namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// The expected outcome of a golden task. The runner diffs the captured
/// timeline against <see cref="TransitionLog"/> and the final status
/// against <see cref="FinalStatus"/>; when <see cref="ExpectsFailure"/>
/// is true, the runner instead asserts the last op raised an
/// <see cref="InvalidOperationException"/> (the aggregate's guard surface).
/// </summary>
/// <param name="FinalStatus">
/// Expected status after all operations resolve. Empty when the task
/// expects an exception instead of a clean run.
/// </param>
/// <param name="TransitionLog">
/// Sequence of statuses the aggregate is expected to pass through, in
/// order — including the entry status the aggregate was created in.
/// </param>
/// <param name="ExpectsFailure">
/// True when the last op is expected to throw (negative test). When
/// true the runner ignores <see cref="FinalStatus"/> and asserts
/// the failure shape against <see cref="ExpectedFailureMessage"/>.
/// </param>
/// <param name="ExpectedFailureMessage">
/// Substring expected to appear in the thrown exception's message.
/// Optional — empty means "any invalid-operation message is fine".
/// </param>
public sealed record EvalExpected(
    string FinalStatus,
    IReadOnlyList<string> TransitionLog,
    bool ExpectsFailure = false,
    string ExpectedFailureMessage = "");
