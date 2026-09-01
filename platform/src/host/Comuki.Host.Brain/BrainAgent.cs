using System.Runtime.CompilerServices;
using Comuki.Host.Brain.Ports;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Brain;

/// <summary>
/// The brain agent loop: a stateless think-run over the leading model
/// with the tool surface (memory.search, catalog/runs/report readers,
/// emit_plan). The loop is manual — not <c>UseFunctionInvocation</c> —
/// because emit_plan must TERMINATE the run and invalid plans get exactly
/// one model-side retry. Progress is streamed as chunks; the final chunk
/// carries the answer/plan JSON.
/// </summary>
/// <param name="chat"></param>
/// <param name="memoryStore"></param>
/// <param name="profileCatalog"></param>
/// <param name="activeRuns"></param>
/// <param name="explorerReports"></param>
/// <param name="options"></param>
public sealed class BrainAgent(
    IChatClient chat,
    IMemoryStore memoryStore,
    IProfileCatalog profileCatalog,
    IActiveRunCatalog activeRuns,
    IExplorerReportReader explorerReports,
    IOptions<BrainOptions> options)
{
    /// <summary>
    /// Runs one brain call and streams its progress. Throws
    /// <see cref="BrainInvalidPlanException"/> /
    /// <see cref="BrainExhaustedException"/>; the gRPC service maps them
    /// to fault statuses.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public async IAsyncEnumerable<BrainChunk> RunAsync(
        BrainRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var toolbox = new BrainToolbox(memoryStore, profileCatalog, activeRuns, explorerReports);
        var chatOptions = new ChatOptions { Tools = [.. toolbox.BuildFunctions()] };

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BrainPrompts.For(request.Kind)),
            new(
                ChatRole.User,
                $"# Task\n{request.Task}\n\n# Context\n{request.ContextJson}"),
        };

        var seq = 0;
        for (var iteration = 1; iteration <= options.Value.MaxToolIterations; iteration++)
        {
            var response = await chat.GetResponseAsync(messages, chatOptions, cancellationToken);
            messages.AddRange(response.Messages);

            var calls = response.Messages
                .SelectMany(static message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToArray();

            if (calls.Length == 0)
            {
                if (request.Kind == BrainRequestKindKeys.Plan)
                {
                    // a plan request answering in prose is a protocol slip:
                    // nudge once; the iteration cap catches a model that
                    // never calls emit_plan
                    messages.Add(new ChatMessage(
                        ChatRole.User,
                        "call emit_plan with the plan JSON — a plain answer is not accepted for plan requests"));
                    continue;
                }

                yield return Final(seq, response.Text);
                yield break;
            }

            var results = new List<AIContent>();
            foreach (var call in calls)
            {
                var result = await BrainToolExecution.ExecuteAsync(toolbox, call, cancellationToken);
                results.Add(new FunctionResultContent(call.CallId, result));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));

            yield return new BrainChunk
            {
                Seq = seq++,
                Text = $"iteration {iteration}: {string.Join(", ", calls.Select(static call => call.Name))}",
            };

            if (toolbox.TryConsumeEmittedPlan(out var planJson))
            {
                yield return Final(seq, planJson);
                yield break;
            }
        }

        throw new BrainExhaustedException(options.Value.MaxToolIterations);

        static BrainChunk Final(int seq, string finalJson) => new() { Seq = seq, FinalJson = finalJson, IsFinal = true };
    }
}

/// <summary>Dispatches one model tool call to the toolbox function it names.</summary>
file static class BrainToolExecution
{
    public static async Task<string> ExecuteAsync(
        BrainToolbox toolbox,
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        var function = toolbox.FindFunction(call.Name)
            ?? throw new InvalidOperationException($"the model called an unknown tool '{call.Name}'");

        var arguments = new Dictionary<string, object?>();
        if (call.Arguments is not null)
        {
            foreach (var pair in call.Arguments)
            {
                arguments[pair.Key] = pair.Value;
            }
        }

        var result = await function.InvokeAsync(new AIFunctionArguments(arguments), cancellationToken);
        return result?.ToString() ?? "tool returned nothing";
    }
}
