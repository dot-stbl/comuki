using Comuki.Modules.Work.Domain;

namespace Comuki.Host.Work;

public sealed partial record WorkTaskRunView
{
    /// <summary>
    /// Build the projection from a loaded <see cref="WorkTask"/> and
    /// its currently-active attempt id. The status field is the Task's
    /// status (Draft / Ready / Active / Blocked / Resolved / Cancelled)
    /// — the underlying Run's own status is hidden behind the
    /// compatibility surface because pre-<c>WorkTask</c> callers
    /// reason in Task terms, not Run terms.
    /// </summary>
    public static WorkTaskRunView From(WorkTask task, Guid activeAttemptId)
    {
        return new WorkTaskRunView(
            TaskId: task.Id.Value,
            AttemptOrdinal: task.AttemptOrdinal.Value,
            RunId: activeAttemptId,
            Status: task.Status.ToString(),
            UpdatedAt: task.UpdatedAt);
    }
}
