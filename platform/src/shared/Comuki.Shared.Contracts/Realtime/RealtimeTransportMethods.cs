namespace Comuki.Shared.Contracts.Realtime;

/// <summary>
/// SignalR client callback names of the realtime surface. The dashboard
/// subscribes to these method names on its <c>@microsoft/signalr</c>
/// connection; renaming any of the constants here is a breaking change on
/// the wire. The constants stay in <see cref="Contracts"/>
/// so the host broadcaster and the codegen emitter see one source of
/// truth — the dashboard TypeScript client reads the same value after the
/// C#→TS contract generator runs.
/// </summary>
public static class RealtimeTransportMethods
{
    /// <summary>Client callback name of the run timeline stream.</summary>
    public const string RunEvent = "RunEvent";

    /// <summary>Client callback name of the project attention stream.</summary>
    public const string Attention = "Attention";

    /// <summary>Client callback name of one live chat progress fragment.</summary>
    public const string ChatChunk = "ChatChunk";

    /// <summary>Client callback name of the terminal signal of one chat turn.</summary>
    public const string ChatTurnComplete = "ChatTurnComplete";
}
