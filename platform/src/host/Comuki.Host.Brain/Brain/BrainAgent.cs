using System.Runtime.CompilerServices;
using Comuki.Host.Brain.Brain.Exceptions;
using Comuki.Host.Brain.Brain.Options;
using Comuki.Host.Brain.Brain.Tools;
using Comuki.Host.Brain.Ports.ActiveRuns;
using Comuki.Host.Brain.Ports.Exploration;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Shared.Contracts.Brain;
using Comuki.Shared.Contracts.ControlPlane.Profiles;
using Comuki.Shared.Contracts.Memory;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Brain.Brain;

/// <summary>
/// The brain agent loop: a stateless think-run over the leading model
/// with the tool surface (memory.search, catalog/runs/report readers,
/// emit_plan). The loop is manual — not <c>UseFunctionInvocation</c> —
/// because emit_plan must TERMINATE the run and invalid plans get exactly
/// one model-side retry. Progress is streamed as chunks; the final chunk
/// carries the answer/plan JSON.
/// <para>
/// Model config is resolved per invocation through
/// <see cref="IModelConfigProvider"/> (issue #53) — a chat client is
/// built from the live result via <see cref="IBrainChatClientFactory"/>
/// and reused across the loop's round-trips. The chat-kind (Answer)
/// requests go through <see cref="ModelConfig.ChatModelId"/> when the
/// operator set <c>ChatModelIdRef</c>; plan / brief / repair keep the
/// flagship <see cref="ModelConfig.ModelId"/>. Rotation in Vault /
/// Consul therefore lands within the resolver's TTL (60s default)
/// without a host restart.
/// </para>
/// </summary>
/// <param name="modelConfig">Per-call model resolution — endpoint / API key / model ids.</param>
/// <param name="chatFactory">Builds the <c>IChatClient</c> from the resolved config.</param>
/// <param name="memoryDigest">
/// Scope-aware digest assembler used to prepend a project/user digest to
/// the caller-built <c>ContextJson</c> when <see cref="BrainRequest.ScopeKind"/>
/// is set. The brain tools (<see cref="BrainToolbox"/>) deliberately stay
/// global-only — see the security note on
/// <see cref="BrainToolbox.SearchMemoryAsync"/> — so the scope-aware
/// fetch lives here, fed by the request, not by anything the model can
/// supply at runtime.
/// </param>
/// <param name="memoryStore">Memory store behind the <c>memory.*</c> tools.</param>
/// <param name="profileCatalog">Control-plane profile catalog exposed as a tool.</param>
/// <param name="activeRuns">Active-run catalog exposed as a tool.</param>
/// <param name="explorerReports">Explorer report reader exposed as a tool.</param>
/// <param name="scopeAccessor">
/// The brain owns no subject — <see cref="BrainRequest"/> carries no
/// caller identity, and there is no per-call project/user context to
/// narrow to — so every run declares itself an explicit system consumer
/// (<c>AsSystem("brain-agent")</c>) for the duration of the loop. This is
/// what lets <c>MemoryDbContext</c>&apos;s scope query filter run at all
/// without throwing; it does not by itself limit what the model can ask
/// <c>memory.search</c> for — see <see cref="BrainToolbox.SearchMemoryAsync"/>
/// for the guard that does that.
/// </param>
/// <param name="clock">The toolbox write clock (custom ephemeral TTLs).</param>
/// <param name="options">Bound brain options — the iteration cap source.</param>
/// <param name="embedder">Optional embedding client — activates the semantic memory path.</param>
public sealed class BrainAgent(
    IModelConfigProvider modelConfig,
    IBrainChatClientFactory chatFactory,
    IMemoryDigest memoryDigest,
    IMemoryStore memoryStore,
    IProfileCatalog profileCatalog,
    IActiveRunCatalog activeRuns,
    IExplorerReportReader explorerReports,
    ISubjectScopeAccessor scopeAccessor,
    TimeProvider clock,
    IOptions<BrainOptions> options,
    IEmbeddingClient? embedder = null)
{
    /// <summary>
    /// Runs one brain call and streams its progress. Throws
    /// <see cref="BrainInvalidPlanException"/> /
    /// <see cref="BrainExhaustedException"/>; the gRPC service maps an
    /// exhausted loop to a fault status and an invalid-after-retry plan to
    /// a graceful final answer carrying the validation errors. Throws
    /// <see cref="ArgumentException"/> when
    /// <see cref="BrainRequest.ScopeKind"/> is set without a parseable
    /// <see cref="BrainRequest.SubjectId"/>.
    /// </summary>
    /// <param name="request">The brain request — task, context JSON and kind.</param>
    /// <param name="cancellationToken">Cancels the run mid-iteration; streamed chunks stop.</param>
    public async IAsyncEnumerable<BrainChunk> RunAsync(
        BrainRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // No subject reaches this call — BrainRequest carries none — so the
        // whole run declares itself a named system consumer up front. A
        // `using` declaration (try/finally, no catch) is the one scope
        // shape that may still enclose the `yield return`s below.
        using var systemScope = scopeAccessor.AsSystem("brain-agent");

        var config = await modelConfig.ResolveAsync(cancellationToken);
        // Chat-kind requests route through the lighter ChatModelId when the
        // operator set ChatModelIdRef; everything else uses the flagship.
        var modelId = request.Kind == BrainRequestKindKeys.Answer
            ? config.ChatModelId
            : config.ModelId;

        var chat = chatFactory.Create(
            new ModelConfig(config.Endpoint, config.ApiKey, modelId, config.ChatModelId));

        var toolbox = new BrainToolbox(memoryStore, clock, profileCatalog, activeRuns, explorerReports, embedder);
        var chatOptions = new ChatOptions { Tools = [.. toolbox.BuildFunctions()] };

        var scopedDigest = await BrainAgentDigestBuilder.BuildAsync(memoryDigest, request, cancellationToken);
        var contextSection = scopedDigest is { } digest
            ? $"# Scoped memory\n{digest}\n\n{request.ContextJson}"
            : request.ContextJson;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BrainPrompts.For(request.Kind)),
            new(
                ChatRole.User,
                $"# Task\n{request.Task}\n\n# Context\n{contextSection}"),
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

/// <summary>
/// Builds the scope-aware digest that <see cref="BrainAgent"/> prepends to
/// the caller-built <c>ContextJson</c> when the incoming
/// <see cref="BrainRequest"/> carries a <c>ScopeKind</c>. Null when
/// <c>ScopeKind</c> is null — the legacy global-only behaviour, the
/// caller-built context is what the model sees and no scope fetch
/// happens. When <c>ScopeKind</c> is set, <c>SubjectId</c> must be a
/// parseable Guid; otherwise the request is malformed and a
/// <see cref="ArgumentException"/> is thrown before any chat
/// round-trip is issued. A static, file-scope helper (not a
/// <see cref="BrainAgent"/> member) keeps the loop's instance surface
/// to contract overrides only.
/// </summary>
file static class BrainAgentDigestBuilder
{
    public static async Task<string?> BuildAsync(
        IMemoryDigest memoryDigest,
        BrainRequest request,
        CancellationToken cancellationToken)
    {
        return request.ScopeKind is null
            ? null
            : string.IsNullOrWhiteSpace(request.SubjectId)
            ? throw new ArgumentException(
                "BrainRequest.SubjectId is required when BrainRequest.ScopeKind is set.",
                nameof(request))
            : Guid.TryParse(request.SubjectId, out var subjectId)
            ? await memoryDigest.BuildDigestAsync(
                new MemoryDigestRequest(request.ScopeKind, subjectId, request.Task),
                cancellationToken)
            : throw new ArgumentException(
                $"BrainRequest.SubjectId must be a valid Guid when ScopeKind is set; got '{request.SubjectId}'.",
                nameof(request));
    }
}
