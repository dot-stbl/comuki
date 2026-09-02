using System.Globalization;
using Comuki.Modules.Chat.Application.Slash;
using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Results;
using Voluta.Graph;

namespace Comuki.Modules.Chat.Application.Graph.Init;

/// <summary>
/// /init wizard node — skeleton edition. Step 0 asks the first prompt; every
/// later turn stores the message as the answer of the current step and asks
/// the next one; after the last step the wizard emits the summary JSON and
/// finishes. Real wiring (project/compute/models/knowledge provisioning) is
/// a documented follow-up.
/// </summary>
public sealed class InitNode : IGraphNode
{
    /// <inheritdoc />
    public Task<NodeResult> InvokeAsync(GraphContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var message = context.Read<string>(ChatChannels.UserMessage) ?? string.Empty;
        var step = InitStepValue.Parse(context.Read<string>(ChatChannels.InitStep));
        var answers = ChatInitWizard.FromAnswersJson(context.Read<string>(ChatChannels.InitAnswersJson) ?? "{}");

        return Task.FromResult(InitNodeLogic.Run(message, step, answers));
    }
}

/// <summary>Pure wizard state machine: message + step + answers → channel writes.</summary>
file static class InitNodeLogic
{
    public static NodeResult Run(string message, int step, IReadOnlyDictionary<string, string> answers)
    {
        return step == 0 ? Ask(ChatInitWizard.Steps[0].Prompt, 1, answers) : Collect(message, step, answers);
    }

    public static NodeResult Ask(string prompt, int nextStep, IReadOnlyDictionary<string, string> answers)
    {
        return NodeResult.Continue(
            new ChannelWrite(ChatChannels.Reply, prompt),
            new ChannelWrite(ChatChannels.InitStep, nextStep.ToString(CultureInfo.InvariantCulture)),
            new ChannelWrite(ChatChannels.InitAnswersJson, ChatInitWizard.ToAnswersJson(answers)),
            new ChannelWrite(ChatChannels.Phase, ChatPhases.Done));
    }

    public static NodeResult Collect(string message, int step, IReadOnlyDictionary<string, string> answers)
    {
        var collected = new Dictionary<string, string>(answers, StringComparer.Ordinal)
        {
            [ChatInitWizard.Steps[step - 1].Key] = message,
        };

        return step >= ChatInitWizard.Steps.Count
            ? Finish(collected)
            : Ask(ChatInitWizard.Steps[step].Prompt, step + 1, collected);
    }

    public static NodeResult Finish(IReadOnlyDictionary<string, string> collected)
    {
        return NodeResult.Continue(
            new ChannelWrite(
                ChatChannels.Reply,
                "Onboarding answers collected (provisioning is a follow-up):\n"
                + ChatInitWizard.ToSummaryJson(collected)),
            new ChannelWrite(ChatChannels.Wizard, "done"),
            new ChannelWrite(ChatChannels.Phase, ChatPhases.Done));
    }
}

file static class InitStepValue
{
    public static int Parse(string? value)
    {
        return int.TryParse(value, out var step) ? step : 0;
    }
}
