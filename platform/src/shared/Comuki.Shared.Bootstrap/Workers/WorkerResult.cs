namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>Outcome of one <see cref="IComukiWorker.ExecuteAsync"/> cycle.</summary>
/// <param name="Success">Whether the cycle achieved its goal; false counts as a failure and backs off.</param>
/// <param name="Detail">One-line summary for logs and the status endpoint.</param>
/// <param name="Data">Optional metric payload surfaced on the status endpoint.</param>
public sealed record WorkerResult(bool Success, string? Detail = null, object? Data = null)
{
    /// <summary>A successful cycle.</summary>
    /// <param name="detail">One-line summary for logs and the status endpoint.</param>
    /// <param name="data">Optional metric payload.</param>
    /// <returns>The result.</returns>
    public static WorkerResult Ok(string? detail = null, object? data = null)
    {
        return new WorkerResult(Success: true, detail, data);
    }

    /// <summary>A failed cycle — the registry logs it and backs off.</summary>
    /// <param name="detail">One-line failure summary.</param>
    /// <returns>The result.</returns>
    public static WorkerResult Fail(string? detail = null)
    {
        return new WorkerResult(Success: false, detail);
    }
}
