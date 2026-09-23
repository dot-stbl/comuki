namespace Comuki.Host.Chat.RunStarter;

/// <summary>
/// Worker launch defaults for chat-created tickets: the claim labels every
/// queued work item carries. v1 reads them from configuration because
/// profiles carry no image metadata yet — resolving image/ref per profile
/// from the project's git settings is a documented follow-up.
/// </summary>
public sealed class ChatWorkerDefaults
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Chat:Worker";

    /// <summary>Worker image chat-created items claim on. The same
    /// configured reference the scale supervisor spawns — an untagged one
    /// is pinned to the running build's version at item-creation time
    /// (see <c>WorkerImagePinning</c>), so both sides of the claim
    /// comparison resolve to the same string.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo chat-created items
    /// claim on. Must equal the scale supervisor's
    /// <c>Compute:Scale:ProfilesGitRef</c> (both default to
    /// <c>main</c>) — the claim SQL compares them for equality.</summary>
    public string ProfilesRef { get; init; } = "main";
}
