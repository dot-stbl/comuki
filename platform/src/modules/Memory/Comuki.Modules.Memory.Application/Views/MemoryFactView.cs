using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;

namespace Comuki.Modules.Memory.Application.Views;

/// <summary>
/// Read-facing projection of a memory fact. Wire/stored keys use the
/// kebab-case strings from the Keys classes; ids are UUIDv7 strings.
/// The embedding vector is deliberately absent — it never leaves the store.
/// </summary>
/// <param name="Id">Fact id (UUIDv7 string).</param>
/// <param name="Scope">Scope key: user | project | global.</param>
/// <param name="SubjectId">Owner id inside the scope.</param>
/// <param name="Kind">Kind key: standing | ephemeral.</param>
/// <param name="TopicKey">Canonicalized topic key.</param>
/// <param name="Text">The fact text.</param>
/// <param name="Source">Source key: chat | human | run | learning-approved.</param>
/// <param name="CreatedBy">Who wrote the fact.</param>
/// <param name="CreatedAt">When the fact was written.</param>
public sealed record MemoryFactView(
    MemoryFactId Id,
    MemoryScope Scope,
    string SubjectId,
    MemoryFactKind Kind,
    string TopicKey,
    string Text,
    MemorySource Source,
    string CreatedBy,
    DateTimeOffset CreatedAt);
