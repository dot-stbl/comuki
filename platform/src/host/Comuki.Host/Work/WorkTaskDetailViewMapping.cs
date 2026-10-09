using Comuki.Modules.Work.Domain;

namespace Comuki.Host.Work;

public partial record WorkTaskDetailView
{
    /// <summary>Project a domain <see cref="WorkTask"/> into the wire response.</summary>
    public static WorkTaskDetailView From(WorkTask task)
    {
        return new WorkTaskDetailView(
            Id: task.Id.Value,
            ProjectId: task.ProjectId.Value,
            Title: task.Title,
            Brief: task.Brief,
            BriefVersion: task.BriefVersion,
            Status: task.Status.ToString(),
            ResolutionOutcome: task.ResolutionOutcome?.ToString(),
            AttemptOrdinal: task.AttemptOrdinal.Value,
            ActiveAttemptId: task.ActiveAttemptId?.Value,
            Visibility: task.Visibility.ToString(),
            MissionId: task.MissionId?.Value,
            SourceRefs: [.. task.SourceRefs.Select(WorkTaskSourceRefView.From)],
            CreatedAt: task.CreatedAt,
            UpdatedAt: task.UpdatedAt);
    }
}
