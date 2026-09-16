using Comuki.Host.Realtime;
using Comuki.Host.Realtime.Broadcasting;
using Comuki.Modules.Chat.Application.Ports;
using Comuki.Modules.Chat.Domain.Ids;
using Comuki.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Unit coverage of the live chat turn fan-out: chunks and terminal signals
/// reach the session's <c>chat:{id}</c> group with the wire method names,
/// outcome words map to their lowercase spellings, and a dead socket is
/// swallowed instead of failing the turn.
/// </summary>
public sealed class SignalRChatTurnProgressShould
{
    private readonly ChatSessionId sessionId = new(new Guid("018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b"));

    private static (SignalRChatTurnProgress Progress, IClientProxy Group) Build(IHubClients? clients = null)
    {
        var groupClient = Substitute.For<IClientProxy>();
        var hubClients = clients ?? Substitute.For<IHubClients>();
        hubClients.Group(Arg.Any<string>()).Returns(groupClient);

        var hubContext = Substitute.For<IHubContext<RunsHub>>();
        hubContext.Clients.Returns(hubClients);

        return (new SignalRChatTurnProgress(hubContext, NullLogger<SignalRChatTurnProgress>.Instance), groupClient);
    }

    [Fact(DisplayName = "Given a running turn fragment, when ChunkAsync is called, then ChatChunk goes to the session group")]
    public async Task SendChunkToSessionGroupAsync()
    {
        var (progress, group) = Build();

        await progress.ChunkAsync(sessionId, 3, "memory.search(\"identity\")", TestContext.Current.CancellationToken);

        await group.Received(1).SendCoreAsync(
            RealtimeTransportMethods.ChatChunk,
            Arg.Is<object?[]>(args => IsChunkView(args, sessionId, 3, "memory.search(\"identity\")")),
            Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Given a terminal outcome, when DoneAsync is called, then ChatTurnComplete carries the lowercase outcome word")]
    [InlineData(ChatTurnDone.Replied, "replied")]
    [InlineData(ChatTurnDone.AwaitingApproval, "awaiting_approval")]
    [InlineData(ChatTurnDone.Failed, "failed")]
    public async Task SendTerminalOutcomeAsync(ChatTurnDone done, string expected)
    {
        var (progress, group) = Build();

        await progress.DoneAsync(sessionId, done, TestContext.Current.CancellationToken);

        await group.Received(1).SendCoreAsync(
            RealtimeTransportMethods.ChatTurnComplete,
            Arg.Is<object?[]>(args => IsOutcomeView(args, expected)),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a hub that throws on send, when ChunkAsync is called, then the fault is swallowed and the turn is unaffected")]
    public async Task SwallowTransportFaultsAsync()
    {
        var clients = Substitute.For<IHubClients>();
        clients.Group(Arg.Any<string>()).Returns(static _ => throw new HubException("socket gone"));
        var (progress, _) = Build(clients);

        await progress.ChunkAsync(sessionId, 0, "fragment", TestContext.Current.CancellationToken);
        await progress.DoneAsync(sessionId, ChatTurnDone.Failed, TestContext.Current.CancellationToken);
    }

    private static bool IsChunkView(object?[] args, ChatSessionId sessionId, int seq, string text)
    {
        return args.Length == 1
            && args[0] is ChatChunkView view
            && view.SessionId == sessionId.Value
            && view.Seq == seq
            && view.Text == text;
    }

    private static bool IsOutcomeView(object?[] args, string expected)
    {
        return args.Length == 1
            && args[0] is ChatTurnCompleteView view
            && view.Outcome == expected;
    }
}
