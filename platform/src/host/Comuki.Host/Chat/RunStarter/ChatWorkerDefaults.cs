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

    /// <summary>Worker image (with digest) chat-created items claim on.</summary>
    public string Image { get; init; } = "ghcr.io/comuki/worker:dev";

    /// <summary>Pinned git ref of the profiles repo chat-created items claim on.</summary>
    public string ProfilesRef { get; init; } = "refs/heads/main";
}
