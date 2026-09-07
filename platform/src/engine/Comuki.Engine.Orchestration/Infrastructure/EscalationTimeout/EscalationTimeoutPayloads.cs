using System.Text.Json;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;

/// <summary>
/// Journal payload builders for the escalation-timeout sweeper. One shape
/// per event family, camelCase via <see cref="JsonSerializerOptions.Web"/>.
/// </summary>
internal static class EscalationTimeoutPayloads
{
    /// <summary>Payload for a sweeper-driven Escalated -> Cancelled transition.</summary>
    /// <param name="runId"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="ageSeconds">how long the run sat in Escalated before the sweep.</param>
    public static string EscalationTimeout(RunId runId, string from, string to, double ageSeconds)
    {
        return JsonSerializer.Serialize(new { runId = runId.Value, from, to, ageSeconds }, JsonSerializerOptions.Web);
    }
}
