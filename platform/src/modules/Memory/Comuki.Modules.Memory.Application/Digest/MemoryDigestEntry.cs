namespace Comuki.Modules.Memory.Application.Digest;

/// <summary>One compact digest line — topic, text and kind key, nothing else.</summary>
/// <param name="TopicKey">Canonicalized topic key of the fact.</param>
/// <param name="Text">The fact text.</param>
/// <param name="Kind">Kind key: standing | ephemeral.</param>
public sealed record MemoryDigestEntry(
    string TopicKey,
    string Text,
    string Kind);
