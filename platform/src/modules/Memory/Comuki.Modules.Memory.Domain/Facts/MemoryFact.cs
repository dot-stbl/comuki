using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Domain.Facts;

/// <summary>
/// One long-term memory fact. Writing a fact with the same
/// (scope, subject, <see cref="TopicKey"/>) supersedes the previous row
/// (its <see cref="SupersededAt"/> is set) instead of deleting it — the
/// superseded history stays for audit and is excluded from default search.
/// The embedding vector is deliberately not part of this entity: it lives
/// in the <c>embedding</c> column, written and queried through raw SQL
/// (pgvector), never materialized in .NET.
/// </summary>
public sealed class MemoryFact
{
    internal MemoryFact()
    {
    }

    /// <summary>Fact id (UUIDv7, client-side).</summary>
    public MemoryFactId Id { get; private set; }

    /// <summary>Who the fact belongs to (user / project / global).</summary>
    public MemoryScope Scope { get; private set; }

    /// <summary>Owner id inside the scope; <c>global</c> for global facts.</summary>
    public string SubjectId { get; private set; } = string.Empty;

    /// <summary>Standing decision or ephemeral task note.</summary>
    public MemoryFactKind Kind { get; private set; }

    /// <summary>Canonicalized topic key — same topic ⇒ write supersedes.</summary>
    public string TopicKey { get; private set; } = string.Empty;

    /// <summary>The fact text.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>How the fact entered memory.</summary>
    public MemorySource Source { get; private set; }

    /// <summary>Who wrote the fact (user id, run id or a system label).</summary>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>When the fact was written.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when a newer fact with the same topic key superseded this one; null while active.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>Canonicalizes a subject id or topic key: trimmed, lower-cased — one shape for entity and query.</summary>
    /// <param name="value"></param>
    public static string CanonicalKey(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>Creates a fact; topic key and subject are canonicalized (trimmed, lower-cased).</summary>
    /// <param name="scope"></param>
    /// <param name="subjectId"></param>
    /// <param name="kind"></param>
    /// <param name="topicKey"></param>
    /// <param name="text"></param>
    /// <param name="source"></param>
    /// <param name="createdBy"></param>
    /// <param name="now"></param>
    /// <exception cref="ArgumentException"></exception>
    public static MemoryFact Create(
        MemoryScope scope,
        string subjectId,
        MemoryFactKind kind,
        string topicKey,
        string text,
        MemorySource source,
        string createdBy,
        DateTimeOffset now)
    {
        return string.IsNullOrWhiteSpace(subjectId)
            ? throw new ArgumentException("subject id must not be empty", nameof(subjectId))
            : string.IsNullOrWhiteSpace(topicKey)
            ? throw new ArgumentException("topic key must not be empty", nameof(topicKey))
            : string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("fact text must not be empty", nameof(text))
            : new MemoryFact
            {
                Id = MemoryFactId.New(),
                Scope = scope,
                SubjectId = CanonicalKey(subjectId),
                Kind = kind,
                TopicKey = CanonicalKey(topicKey),
                Text = text.Trim(),
                Source = source,
                CreatedBy = createdBy.Trim(),
                CreatedAt = now,
            };
    }

    /// <summary>Marks the fact superseded by a newer write on the same topic.</summary>
    /// <param name="now"></param>
    public void Supersede(DateTimeOffset now)
    {
        SupersededAt = now;
    }
}
