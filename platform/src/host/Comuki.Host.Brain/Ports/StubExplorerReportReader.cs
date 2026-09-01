namespace Comuki.Host.Brain.Ports;

/// <summary>Slice-A stub: no explorer reports are available through it.</summary>
public sealed class StubExplorerReportReader : IExplorerReportReader
{
    /// <inheritdoc />
    public Task<string?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
