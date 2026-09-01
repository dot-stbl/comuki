using System.Text.Json;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Shared.Contracts.Plans;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;

namespace Comuki.Host.Chat;

/// <summary>
/// Host implementation of the chat tool port — the only place the chat
/// graph touches orchestration. Singleton (graph nodes resolve it from the
/// root provider) with a scope per call: the scoped orchestration services
/// (runs reader, run starter) live inside that scope. Tools:
/// <c>runs</c> (list), <c>create_ticket</c> (apply a validated plan),
/// <c>stop_run</c> (honest not-implemented stub — run control lands with
/// the run-management slice).
/// </summary>
/// <param name="scopeFactory">Scope factory over the host provider.</param>
public sealed class HostChatToolExecutor(IServiceScopeFactory scopeFactory) : IChatToolExecutor
{
    /// <summary>Tool name: list recent runs.</summary>
    public const string RunsTool = "runs";

    /// <summary>Tool name: apply an approved plan as a new run.</summary>
    public const string CreateTicketTool = "create_ticket";

    /// <summary>Tool name: stop a run (stub).</summary>
    public const string StopRunTool = "stop_run";

    /// <summary>Runs the tool matching the call name; unknown names fail with data.</summary>
    /// <param name="call"></param>
    /// <param name="cancellationToken"></param>
    public async Task<ChatToolResult> ExecuteAsync(ChatToolCall call, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        return call.Name switch
        {
            RunsTool => await HostChatTools.ListRunsAsync(scope.ServiceProvider, cancellationToken),
            CreateTicketTool => await HostChatTools.CreateTicketAsync(scope.ServiceProvider, call.ArgumentsJson, cancellationToken),
            StopRunTool => HostChatTools.StopRunStub(),
            _ => HostChatTools.UnknownTool(call.Name),
        };
    }
}

/// <summary>Tool bodies over a per-call service scope.</summary>
file static class HostChatTools
{
    public static async Task<ChatToolResult> ListRunsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var reader = services.GetRequiredService<IRunsReader>();
        var runs = await reader.ListRecentAsync(10, cancellationToken);

        return new ChatToolResult(
            true,
            JsonSerializer.Serialize(new RunsPayload([.. runs]), JsonSerializerOptions.Web),
            null,
            NotImplemented: false);
    }

    public static async Task<ChatToolResult> CreateTicketAsync(
        IServiceProvider services,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        var ticket = ChatTicketArgumentParsing.Parse(argumentsJson);

        if (ticket is null)
        {
            return new ChatToolResult(false, "{}", "chat.ticket_arguments_invalid", NotImplemented: false);
        }

        var validation = await services.GetRequiredService<IValidator<Plan>>().ValidateAsync(ticket.Plan, cancellationToken);

        if (!validation.IsValid)
        {
            return new ChatToolResult(false, "{}", "chat.plan_invalid", NotImplemented: false);
        }

        var starter = services.GetRequiredService<ChatRunStarter>();
        var runId = await starter.StartAsync(new ProjectId(Guid.Parse(ticket.ProjectId)), ticket.Plan, cancellationToken);

        return new ChatToolResult(
            true,
            JsonSerializer.Serialize(new RunIdPayload(runId.Value.ToString()), JsonSerializerOptions.Web),
            null,
            NotImplemented: false);
    }

    public static ChatToolResult StopRunStub()
    {
        // honest stub: run control (stop/inject) is its own slice; do not pretend it ran
        return new ChatToolResult(
            false,
            "{}",
            "chat.tool_not_implemented",
            NotImplemented: true);
    }

    public static ChatToolResult UnknownTool(string name)
    {
        return new ChatToolResult(false, "{}", "chat.tool_unknown:" + name, NotImplemented: false);
    }
}

/// <summary>runs tool payload (camelCase).</summary>
/// <param name="Runs"></param>
internal sealed record RunsPayload(IReadOnlyList<RunSummary> Runs);

/// <summary>create_ticket success payload (camelCase).</summary>
/// <param name="RunId"></param>
internal sealed record RunIdPayload(string RunId);
