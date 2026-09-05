using System.ComponentModel.DataAnnotations;

namespace Comuki.Modules.Knowledge.Infrastructure.Configuration;

/// <summary>
/// Configuration for the doc worker ingest loop and chunking knobs.
/// <see cref="ChunkTokenTarget"/> is the soft chunk size in tokens
/// (approximated as whitespace-separated word count ÷ 0.75 — a deliberately
/// cheap estimator since the chunker doesn't run a tokenizer); the
/// chunker splits paragraphs and packs them up to that target.
/// </summary>
public sealed class KnowledgeIngestOptions
{
    /// <summary>Configuration section name (used by the host installer).</summary>
    public const string SectionName = "Knowledge:Ingest";

    /// <summary>Target chunk size in estimated tokens (default ~500).</summary>
    [Range(64, 8192)]
    public int ChunkTokenTarget { get; init; } = 500;

    /// <summary>Background poll interval — the doc worker only runs when poll is enabled.</summary>
    [Range(1, 86_400)]
    public int PollIntervalSeconds { get; init; } = 60;
}
