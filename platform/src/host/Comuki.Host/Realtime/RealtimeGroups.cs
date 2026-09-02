using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Realtime;

/// <summary>
/// SignalR group names of the realtime surface (issue #7, scope-draft §8):
/// every journal append fans out to <c>run:{id}</c>, and attention-worthy
/// transitions additionally to <c>project:{id}:attention</c>. Ids render in
/// the lowercase <c>D</c> GUID format so group names are stable strings.
/// </summary>
public static class RealtimeGroups
{
    /// <summary>The timeline group of one run — join requires <c>run:read</c> on the run's project.</summary>
    /// <param name="runId"></param>
    /// <returns></returns>
    public static string RunGroup(RunId runId)
    {
        return "run:" + runId.Value.ToString("D");
    }

    /// <summary>The attention group of one project — join requires <c>project:read</c> on that project.</summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public static string ProjectAttentionGroup(ProjectId projectId)
    {
        return "project:" + projectId.Value.ToString("D") + ":attention";
    }
}
