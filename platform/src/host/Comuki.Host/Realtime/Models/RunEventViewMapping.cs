using System.Text.Json;
using Comuki.Shared.Contracts.Journal;

namespace Comuki.Host.Realtime.Models;

/// <summary>
/// Journal entry → <see cref="RunEventView"/>: extracts the work item id
/// from work-item payloads and enforces the payload size cap. Boundary
/// parse — payload shapes are per-type and open, so a miss is a null, never
/// an exception.
/// </summary>
internal static class RunEventViewMapping
{
    /// <summary>
    /// Payloads above this many characters are omitted from the hub message
    /// (complete/fail entries embed worker results, which can be large);
    /// the client refetches the timeline over REST instead.
    /// </summary>
    public const int MaxPayloadJsonChars = 32_768;

    /// <summary>Maps one journal entry to its slim broadcast shape.</summary>
    /// <param name="entry"></param>
    /// <returns></returns>
    public static RunEventView ToView(RunEventEntry entry)
    {
        var payloadOmitted = entry.PayloadJson.Length > MaxPayloadJsonChars;

        return new RunEventView(
            entry.RunId.Value,
            entry.Type,
            ReadWorkItemId(entry),
            entry.OccurredAt.ToUnixTimeMilliseconds(),
            payloadOmitted ? null : entry.PayloadJson,
            payloadOmitted);
    }

    /// <summary>
    /// Reads <c>itemId</c> out of a work-item payload; run events and
    /// unparsable payloads yield null.
    /// </summary>
    /// <param name="entry"></param>
    /// <returns></returns>
    public static Guid? ReadWorkItemId(RunEventEntry entry)
    {
        return !entry.Type.StartsWith("work_item.", StringComparison.Ordinal) || !TryParse(entry.PayloadJson, out var itemId) ? null : itemId;
    }

    /// <summary>Extracts the <c>itemId</c> property of a payload document; false when absent or not a guid.</summary>
    /// <param name="payloadJson"></param>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public static bool TryParse(string payloadJson, out Guid itemId)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            itemId = default;
            return document.RootElement.TryGetProperty("itemId", out var value)
                && value.ValueKind == JsonValueKind.String
                && Guid.TryParse(value.GetString(), out itemId);
        }
        catch (JsonException)
        {
            itemId = Guid.Empty;
            return false;
        }
    }
}
