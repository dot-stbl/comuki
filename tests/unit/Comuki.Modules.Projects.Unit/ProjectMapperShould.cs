using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Mapper fidelity: every entity field lands in its view slot, nullability
/// rides along, and the settings version passes through unchanged (clients
/// echo it into the next PUT).
/// </summary>
public sealed class ProjectMapperShould
{
    private readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a project, when mapped, then the view mirrors every field including the optional ones")]
    public void MapProjectWithAllFields()
    {
        var project = Project.Create(
            "  Web Platform  ",
            "WEB-Platform",
            "customer portal",
            "https://git.example.com/acme/profiles.git",
            "refs/tags/v1",
            now);

        var view = ProjectMapper.ToView(project);

        view.Id.ShouldBe(project.Id);
        view.Name.ShouldBe("Web Platform");
        view.Slug.ShouldBe("web-platform");
        view.Description.ShouldBe("customer portal");
        view.ProfilesGitUrl.ShouldBe("https://git.example.com/acme/profiles.git");
        view.ProfilesGitRef.ShouldBe("refs/tags/v1");
        view.Archived.ShouldBeFalse();
        view.CreatedAt.ShouldBe(now);
        view.UpdatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a project without optional fields, when mapped, then the view keeps them null")]
    public void MapProjectWithoutOptionalFields()
    {
        var project = Project.Create("Backend", "backend", null, null, null, now);

        var view = ProjectMapper.ToView(project);

        view.Description.ShouldBeNull();
        view.ProfilesGitUrl.ShouldBeNull();
        view.ProfilesGitRef.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an archived project, when mapped, then the archive flag and timestamp ride along")]
    public void MapArchivedProject()
    {
        var project = Project.Create("Backend", "backend", null, null, null, now);
        project.Archive(now.AddDays(1));

        var view = ProjectMapper.ToView(project);

        view.Archived.ShouldBeTrue();
        view.ArchivedAt.ShouldBe(now.AddDays(1));
    }

    [Fact(DisplayName = "Given default settings, when mapped, then the view carries the defaults and version 1")]
    public void MapDefaultSettings()
    {
        var projectId = ProjectId.New();
        var settings = ProjectSettings.CreateDefaults(projectId, now);

        var view = ProjectMapper.ToView(settings);

        view.ProjectId.ShouldBe(projectId);
        view.MinIdle.ShouldBe(0);
        view.MaxConcurrent.ShouldBe(ProjectSettings.DefaultMaxConcurrent);
        view.IdleTtlSeconds.ShouldBeNull();
        view.ApproveRequired.ShouldBeFalse();
        view.KnowledgeEnabled.ShouldBeFalse();
        view.VerifyEnabled.ShouldBeFalse();
        view.ProxyEnabled.ShouldBeFalse();
        view.Version.ShouldBe(1);
    }

    [Fact(DisplayName = "Given applied settings, when mapped, then the new values and the bumped version ride along")]
    public void MapAppliedSettings()
    {
        var projectId = ProjectId.New();
        var settings = ProjectSettings.CreateDefaults(projectId, now);
        settings.Apply(2, 16, 1800, true, true, false, true, now.AddMinutes(5));

        var view = ProjectMapper.ToView(settings);

        view.MinIdle.ShouldBe(2);
        view.MaxConcurrent.ShouldBe(16);
        view.IdleTtlSeconds.ShouldBe(1800);
        view.ApproveRequired.ShouldBeTrue();
        view.KnowledgeEnabled.ShouldBeTrue();
        view.VerifyEnabled.ShouldBeFalse();
        view.ProxyEnabled.ShouldBeTrue();
        view.UpdatedAt.ShouldBe(now.AddMinutes(5));
        view.Version.ShouldBe(2);
    }
}
