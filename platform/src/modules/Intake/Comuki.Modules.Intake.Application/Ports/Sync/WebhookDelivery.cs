namespace Comuki.Modules.Intake.Application.Ports.Sync;

/// <summary>
/// One raw webhook delivery as it arrived: the unmodified body bytes
/// (signature input — never re-serialized), the request headers and the
/// query parameters (Jira's shared-secret query param lives here).
/// </summary>
/// <param name="Body">Raw request body — the exact bytes the signature was computed over.</param>
/// <param name="Headers">Request headers (last value wins for duplicates).</param>
/// <param name="Query">Query parameters of the hook URL.</param>
public sealed record WebhookDelivery(
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Query)
{
    /// <summary>Case-insensitive header lookup; null when absent.</summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public string? Header(string name)
    {
        return Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Case-insensitive query lookup; null when absent.</summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public string? QueryParam(string name)
    {
        return Query.TryGetValue(name, out var value) ? value : null;
    }
}
