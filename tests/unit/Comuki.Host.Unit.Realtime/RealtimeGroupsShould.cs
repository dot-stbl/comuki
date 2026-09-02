using Comuki.Host.Realtime;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>
/// Group-name contract of the realtime surface: the scope-draft pins
/// <c>run:{id}</c> and <c>project:{id}:attention</c>, ids in lowercase
/// GUID format.
/// </summary>
public sealed class RealtimeGroupsShould
{
    [Fact(DisplayName = "Given a run id, when RunGroup is called, then the name is run:{id} in lowercase D format")]
    public void RenderRunGroup()
    {
        var runId = new RunId(new Guid("018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b"));

        RealtimeGroups.RunGroup(runId).ShouldBe("run:018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b");
    }

    [Fact(DisplayName = "Given a project id, when ProjectAttentionGroup is called, then the name is project:{id}:attention")]
    public void RenderProjectAttentionGroup()
    {
        var projectId = new ProjectId(new Guid("018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b"));

        RealtimeGroups.ProjectAttentionGroup(projectId).ShouldBe("project:018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b:attention");
    }

    [Fact(DisplayName = "Given uppercase guid text, when group names render, then the id is lowercase")]
    public void RenderLowercaseIds()
    {
        var runId = new RunId(new Guid("018F1E2B-3C4D-5E6F-7A8B-9C0D1E2F3A4B"));

        RealtimeGroups.RunGroup(runId).ShouldBe("run:018f1e2b-3c4d-5e6f-7a8b-9c0d1e2f3a4b");
    }
}
