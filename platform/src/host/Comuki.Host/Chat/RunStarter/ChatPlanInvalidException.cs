using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Host.Chat.RunStarter;

/// <summary>
/// Thrown by <see cref="ChatRunStarter.StartAsync"/> when the plan fails
/// <see cref="PlanValidator"/> — the precondition <c>ChatRunStarter</c>
/// depends on (unique node ids, edges referencing only known nodes, no
/// self-loops or duplicate edges, acyclic). Without this guard an edge to
/// an unknown node throws an unhandled <see cref="KeyNotFoundException"/>
/// and a duplicate edge violates the <c>WorkItemDependency</c> composite
/// primary key at <c>SaveChangesAsync</c> instead of failing typed here.
/// </summary>
/// <param name="errors">The validator's error list, joined into the message.</param>
public sealed class ChatPlanInvalidException(IReadOnlyList<string> errors)
    : DomainException("chat.plan_invalid", "plan is invalid: " + string.Join("; ", errors))
{
    /// <summary>The validator's raw error list.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}
