using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;

namespace Comuki.Modules.Memory.Application.Ports;

/// <summary>
/// One long-term fact write. The store supersedes the previous active row
/// with the same (scope, subject, topic key) transactionally and stores the
/// embedding vector when one is supplied — embeddings are computed by the
/// caller (MEAI or off) and are optional: facts and search keep working
/// without them via the fallback ranking.
/// </summary>
/// <param name="Scope">Who the fact belongs to.</param>
/// <param name="SubjectId">Owner id inside the scope; <c>global</c> for global facts.</param>
/// <param name="Kind">Standing decision or ephemeral task note.</param>
/// <param name="TopicKey">Canonicalized topic — same topic supersedes.</param>
/// <param name="Text">The fact text.</param>
/// <param name="Source">How the fact entered memory.</param>
/// <param name="CreatedBy">Who wrote it (user id, run id or a system label).</param>
/// <param name="Embedding">Optional query-time-ready vector; null leaves the column empty.</param>
public sealed record MemoryFactWrite(
    MemoryScope Scope,
    string SubjectId,
    MemoryFactKind Kind,
    string TopicKey,
    string Text,
    MemorySource Source,
    string CreatedBy,
    float[]? Embedding = null);
