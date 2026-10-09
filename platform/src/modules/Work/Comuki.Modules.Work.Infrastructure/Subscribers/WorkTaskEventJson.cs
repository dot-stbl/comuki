using System.Text.Json;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Generic "best-effort JSON deserialise" seam — shared by every
/// Work-side subscriber that pulls envelopes off the engine outbox.
/// Each subscriber used to ship its own
/// <c>try { Deserialize&lt;...&gt; } catch (JsonException) { return null; }</c>
/// block; this helper folds them into one place so a future change
/// to the parsing strategy (system-source-gen context, shape-level
/// guard, structured-log instrument) lands once instead of in four
/// copies. Returns <c>null</c> on malformed JSON
/// (<see cref="JsonException"/>) — exactly the poison-row semantics
/// each subscriber wanted.
/// </summary>
internal static class WorkTaskEventJson
{
    /// <summary>
    /// Deserialise <paramref name="payload"/> as <typeparamref name="T"/>
    /// with <see cref="JsonSerializerOptions.Web"/>. Returns
    /// <c>null</c> on JSON-shape or type-mismatch failures.
    /// </summary>
    public static T? TryDeserialize<T>(string payload)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
