namespace Comuki.Engine.Orchestration.Application.MergeQueue;

/// <summary>Paged list view — items + total in scope.</summary>
public sealed record MergeQueuePage
{
    /// <summary>Items on this page.</summary>
    public required IReadOnlyList<MergeQueueEntryView> Items { get; init; }

    /// <summary>Total count in the queried scope (not just this page).</summary>
    public required int Total { get; init; }
}
