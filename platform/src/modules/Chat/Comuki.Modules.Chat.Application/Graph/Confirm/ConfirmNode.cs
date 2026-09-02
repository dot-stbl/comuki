using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph.Confirm;

/// <summary>
/// Confirm node — the HITL gate. First visit interrupts with the approve
/// card (the plan JSON, wire-safe as a string); the resume command payload
/// routes the decision: <c>approve</c> continues to act, anything else is a
/// rejection (the payload doubles as the reason).
/// </summary>
public sealed class ConfirmNode : IGraphNode
{
    /// <summary>Resume payload of an approve decision.</summary>
    public const string ApprovePayload = "approve";

    /// <inheritdoc />
    public Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var planJson = context.Read<string>(ChatChannels.PlanJson) ?? string.Empty;

        return Task.FromResult<NodeResult>(
            context.ResumePayload is not string decision
                ? NodeResult.Interrupt(planJson)
                : decision == ApprovePayload
                    ? NodeResult.Continue(new ChannelWrite(ChatChannels.Phase, ChatPhases.Act))
                    : NodeResult.Continue(
                        new ChannelWrite(ChatChannels.Reply, ChatRejectionText.Of(decision)),
                        new ChannelWrite(ChatChannels.Phase, ChatPhases.Done)));
    }
}

file static class ChatRejectionText
{
    public static string Of(string decision)
    {
        return decision.Length == 0
            ? "Plan rejected — nothing was queued."
            : "Plan rejected — nothing was queued. Reason: " + decision;
    }
}
