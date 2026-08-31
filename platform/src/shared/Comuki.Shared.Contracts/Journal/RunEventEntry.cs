using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Journal;

/// <summary>
/// One append-only run timeline entry, as read or written through
/// <see cref="IRunJournal"/>. <see cref="PayloadJson"/> is raw JSON — its
/// shape is open and per-<see cref="Type"/>.
/// </summary>
/// <param name="Id"></param>
/// <param name="RunId"></param>
/// <param name="Type"></param>
/// <param name="PayloadJson"></param>
/// <param name="OccurredAt"></param>
public sealed record RunEventEntry(
    Guid Id,
    RunId RunId,
    string Type,
    string PayloadJson,
    DateTimeOffset OccurredAt);
