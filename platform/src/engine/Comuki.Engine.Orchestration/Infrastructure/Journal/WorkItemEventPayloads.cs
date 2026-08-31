using System.Text.Json;
using Comuki.Engine.Orchestration.Domain;

namespace Comuki.Engine.Orchestration.Infrastructure.Journal;

/// <summary>
/// Journal payload builders shared by the queue implementation and the lease
/// reaper — one shape per event family, camelCase via
/// <see cref="JsonSerializerOptions.Web"/>.
/// </summary>
internal static class WorkItemEventPayloads
{
    /// <summary>Payload for a claim-driven status change (queued -> running).</summary>
    /// <param name="itemId"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="workerId"></param>
    /// <param name="attempt"></param>
    public static string StatusChanged(Guid itemId, string from, string to, Guid workerId, int attempt)
    {
        return JsonSerializer.Serialize(new { itemId, from, to, workerId, attempt }, JsonSerializerOptions.Web);
    }

    /// <summary>Payload for a worker-driven terminal transition, embedding the result/reason detail.</summary>
    /// <param name="itemId"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <param name="detail"></param>
    public static string StatusChangedWithDetail(Guid itemId, string from, string to, object detail)
    {
        return JsonSerializer.Serialize(new { itemId, from, to, detail }, JsonSerializerOptions.Web);
    }

    /// <summary>Payload for a reaped lease (running -> queued requeue or running -> failed).</summary>
    /// <param name="itemId"></param>
    /// <param name="to"></param>
    /// <param name="attempt"></param>
    public static string LeaseExpired(Guid itemId, string to, int attempt)
    {
        return JsonSerializer.Serialize(new { itemId, from = nameof(WorkItemStatus.Running), to, attempt }, JsonSerializerOptions.Web);
    }
}
