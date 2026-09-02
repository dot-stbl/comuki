namespace Comuki.Host.Brain.Ports.Exploration;

/// <summary>
/// The brain's view of the latest explorer (read-only recon) report —
/// the <c>read_explorer_report</c> tool. Slice A ships a stub; the real
/// reader arrives with the run journal surface.
/// </summary>
public interface IExplorerReportReader
{
    /// <summary>The latest explorer report text, or null when none exists yet.</summary>
    /// <param name="cancellationToken"></param>
    public Task<string?> ReadLatestAsync(CancellationToken cancellationToken = default);
}
