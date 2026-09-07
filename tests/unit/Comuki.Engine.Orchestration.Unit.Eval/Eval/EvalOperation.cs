namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// A single step in a golden task: which <see cref="EvalAction"/> to apply
/// and, where the action carries a target status, the target. The runner
/// reads <see cref="Status"/> only for <see cref="EvalAction.Transition"/>
/// and <see cref="EvalAction.TransitionExpectFailure"/>.
/// </summary>
/// <param name="Action">The action to apply to the aggregate.</param>
/// <param name="Status">
/// Target status for transition-style actions; unused for create / lease ops.
/// Stored as a string so the JSON stays human-readable and engine-internal
/// status enums don't leak into fixtures.
/// </param>
public sealed record EvalOperation(EvalAction Action, string Status);
