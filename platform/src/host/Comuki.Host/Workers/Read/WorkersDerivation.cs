namespace Comuki.Host.Workers.Read;

/// <summary>
/// Result of the derived-workers query: the paged rows plus the total
/// before paging (the paging envelope's <c>Total</c>).
/// </summary>
/// <param name="Rows">Page of derived workers (busy first, then journal-idle).</param>
/// <param name="Total">Total derived workers across all pages.</param>
public sealed record WorkersDerivation(IReadOnlyList<WorkerView> Rows, int Total);
