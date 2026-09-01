namespace Comuki.Shared.Contracts.Brain;

/// <summary>
/// One invocation of the brain agent-loop. <paramref name="Kind"/> picks the
/// mode (<c>plan</c> — decompose the task into a plan; <c>chat</c> — free-form
/// reply); <paramref name="ContextJson"/> carries the assembled context
/// (memory digest, history tail, project facts) as JSON; the caller owns
/// assembly, the brain itself stays stateless.
/// </summary>
/// <param name="Kind">Invocation mode: <c>plan</c> or <c>chat</c>.</param>
/// <param name="ContextJson">Assembled context as JSON (digest, history, facts).</param>
/// <param name="Task">The task or question for the brain.</param>
public sealed record BrainRequest(string Kind, string ContextJson, string Task);
