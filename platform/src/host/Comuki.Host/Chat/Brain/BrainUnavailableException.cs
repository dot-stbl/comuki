namespace Comuki.Host.Chat.Brain;

/// <summary>
/// The brain host is configured but the call did not complete — the
/// process is down, the deadline expired, or the loop faulted. The turn
/// fails honestly (503) instead of the orchestrator inventing a reply;
/// the message never carries the endpoint or any credential.
/// </summary>
/// <param name="code">Stable machine code (the gRPC status name).</param>
/// <param name="detail">Operator-facing detail, already scrubbed.</param>
/// <param name="innerException">The transport fault.</param>
public sealed class BrainUnavailableException(string code, string detail, Exception? innerException = null)
    : Exception(detail, innerException)
{
    /// <summary>Problem code surfaced to the caller.</summary>
    public const string ProblemCode = "chat.brain_unavailable";

    /// <summary>gRPC status name the call failed with.</summary>
    public string Code { get; } = code;
}
