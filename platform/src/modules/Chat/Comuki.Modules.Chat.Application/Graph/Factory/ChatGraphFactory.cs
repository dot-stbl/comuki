using Comuki.Modules.Chat.Application.Graph.Act;
using Comuki.Modules.Chat.Application.Graph.Catalog;
using Comuki.Modules.Chat.Application.Graph.Channels;
using Comuki.Modules.Chat.Application.Graph.Clarify;
using Comuki.Modules.Chat.Application.Graph.Confirm;
using Comuki.Modules.Chat.Application.Graph.Init;
using Comuki.Modules.Chat.Application.Graph.Route;
using Comuki.Modules.Chat.Application.Graph.Think;
using Voluta;
using Voluta.Abstractions.Channels;
using Voluta.Abstractions.Checkpoint;
using Voluta.Graph;
using Voluta.Graph.Builder;
using Voluta.Graph.Options;

namespace Comuki.Modules.Chat.Application.Graph.Factory;

/// <summary>
/// Builds the compiled chat graph: route → clarify | init | think →
/// confirm (HITL interrupt) → act → END. Nodes resolve their ports from
/// the host service provider passed via <see cref="CompileOptions.Services"/>.
/// </summary>
public static class ChatGraphFactory
{
    /// <summary>Compiles the chat graph over the given checkpointer.</summary>
    /// <param name="services">Host provider the nodes resolve ports from.</param>
    /// <param name="checkpointer">Checkpoint store (thread per session id).</param>
    public static CompiledGraph Compile(IServiceProvider services, ICheckpointer checkpointer)
    {
        return new StateGraph()
            .AddChannel(ChatChannels.UserMessage, ChannelKind.LastValue)
            .AddChannel(ChatChannels.Task, ChannelKind.LastValue)
            .AddChannel(ChatChannels.Phase, ChannelKind.LastValue)
            .AddChannel(ChatChannels.BrainKind, ChannelKind.LastValue)
            .AddChannel(ChatChannels.Reply, ChannelKind.LastValue)
            .AddChannel(ChatChannels.Digest, ChannelKind.LastValue)
            .AddChannel(ChatChannels.PlanJson, ChannelKind.LastValue)
            .AddChannel(ChatChannels.RunId, ChannelKind.LastValue)
            .AddChannel(ChatChannels.SessionId, ChannelKind.LastValue)
            .AddChannel(ChatChannels.SubjectId, ChannelKind.LastValue)
            .AddChannel(ChatChannels.ProjectId, ChannelKind.LastValue)
            .AddChannel(ChatChannels.Wizard, ChannelKind.LastValue)
            .AddChannel(ChatChannels.InitStep, ChannelKind.LastValue)
            .AddChannel(ChatChannels.InitAnswersJson, ChannelKind.LastValue)
            .AddChannel(ChatChannels.ToolName, ChannelKind.LastValue)
            .AddChannel(ChatChannels.ToolResult, ChannelKind.LastValue)
            .AddNode<RouteNode>("route")
            .AddNode<ClarifyNode>("clarify")
            .AddNode<InitNode>("init")
            .AddNode<ThinkNode>("think")
            .AddNode<ConfirmNode>("confirm")
            .AddNode<ActNode>("act")
            .AddEdge(GraphConstants.Start, "route")
            .AddConditionalEdges(
                "route",
                static context => context.Read<string>(ChatChannels.Phase) switch
                {
                    ChatPhases.Clarify => "clarify",
                    ChatPhases.Init => "init",
                    _ => "think",
                })
            .AddEdge("clarify", GraphConstants.End)
            .AddEdge("init", GraphConstants.End)
            .AddConditionalEdges(
                "think",
                static context => context.Read<string>(ChatChannels.Phase) == ChatPhases.Confirm ? "confirm" : GraphConstants.End)
            .AddConditionalEdges(
                "confirm",
                static context => context.Read<string>(ChatChannels.Phase) == ChatPhases.Act ? "act" : GraphConstants.End)
            .AddEdge("act", GraphConstants.End)
            .Compile(checkpointer, new CompileOptions { RecursionLimit = 32, Services = services });
    }
}
