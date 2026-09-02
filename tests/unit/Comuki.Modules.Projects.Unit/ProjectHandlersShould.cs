using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Projects.Archive;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Projects.Queries;
using Comuki.Modules.Projects.Application.Projects.Update;
using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Application handlers over mocked ports: create/update/archive/list/get
/// and settings get/update including conflict and not-found paths.
/// </summary>
public sealed class ProjectHandlersShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly IProjectStore projects = Substitute.For<IProjectStore>();
    private readonly IProjectSettingsStore settings = Substitute.For<IProjectSettingsStore>();
    private readonly FakeTime clock;

    public ProjectHandlersShould()
    {
        clock = new FakeTime(now);
    }

    [Fact(DisplayName = "Given a free slug, when Create runs, then project and defaults are persisted")]
    public async Task CreatePersistsProjectAndDefaultsAsync()
    {
        projects.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns((Project?)null);
        var handler = new CreateProjectHandler(projects, clock);

        var view = await handler.HandleAsync(
            new CreateProjectCommand("Acme", "Acme", "d", "git://x", "main"),
            TestContext.Current.CancellationToken);

        view.Slug.ShouldBe("acme");
        view.Name.ShouldBe("Acme");
        await projects.Received(1).AddAsync(
            Arg.Is<Project>(static project => project.Slug == "acme" && project.Name == "Acme"),
            Arg.Is<ProjectSettings>(static row => row.Version == 1 && row.MaxConcurrent == ProjectSettings.DefaultMaxConcurrent),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a taken slug, when Create runs, then ProjectConflictException is thrown")]
    public async Task CreateRefusesDuplicateSlugAsync()
    {
        var existing = Project.Create("Taken", "taken", null, null, null, now);
        projects.FindBySlugAsync("taken", Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new CreateProjectHandler(projects, clock);

        await Should.ThrowAsync<ProjectConflictException>(
            () => handler.HandleAsync(new CreateProjectCommand("X", "Taken", null, null, null), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an existing project, when Update runs, then SaveAsync is called with patched fields")]
    public async Task UpdatePersistsPatchAsync()
    {
        var project = Project.Create("Old", "old", "a", null, null, now);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var handler = new UpdateProjectHandler(projects, clock);

        var view = await handler.HandleAsync(
            new UpdateProjectCommand(project.Id, "New", null, "git://n", "n"),
            TestContext.Current.CancellationToken);

        view.Name.ShouldBe("New");
        view.ProfilesGitUrl.ShouldBe("git://n");
        await projects.Received(1).SaveAsync(project, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a missing project, when Update runs, then ProjectNotFoundException is thrown")]
    public async Task UpdateThrowsWhenMissingAsync()
    {
        var id = ProjectId.New();
        projects.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Project?)null);
        var handler = new UpdateProjectHandler(projects, clock);

        var exception = await Should.ThrowAsync<ProjectNotFoundException>(
            () => handler.HandleAsync(new UpdateProjectCommand(id, "n", null, null, null), TestContext.Current.CancellationToken));
        exception.ProjectId.ShouldBe(id);
    }

    [Fact(DisplayName = "Given an existing project, when Archive runs, then the project is archived and saved")]
    public async Task ArchivePersistsAsync()
    {
        var project = Project.Create("P", "p", null, null, null, now);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var handler = new ArchiveProjectHandler(projects, clock);

        var view = await handler.HandleAsync(new ArchiveProjectCommand(project.Id), TestContext.Current.CancellationToken);

        view.Archived.ShouldBeTrue();
        await projects.Received(1).SaveAsync(project, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given projects in the store, when List runs, then views are mapped")]
    public async Task ListMapsViewsAsync()
    {
        var first = Project.Create("A", "a", null, null, null, now);
        var second = Project.Create("B", "b", null, null, null, now);
        projects.ListAsync(false, Arg.Any<CancellationToken>()).Returns([first, second]);
        var handler = new ListProjectsHandler(projects);

        var views = await handler.HandleAsync(false, TestContext.Current.CancellationToken);

        views.Count.ShouldBe(2);
        views[0].Slug.ShouldBe("a");
        views[1].Slug.ShouldBe("b");
    }

    [Fact(DisplayName = "Given an existing project, when Get runs, then the view is returned")]
    public async Task GetReturnsViewAsync()
    {
        var project = Project.Create("A", "a", null, null, null, now);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        var handler = new GetProjectHandler(projects);

        var view = await handler.HandleAsync(project.Id, TestContext.Current.CancellationToken);

        view.Id.ShouldBe(project.Id);
        view.Slug.ShouldBe("a");
    }

    [Fact(DisplayName = "Given settings, when GetSettings runs, then the view carries version")]
    public async Task GetSettingsReturnsViewAsync()
    {
        var projectId = ProjectId.New();
        var row = ProjectSettings.CreateDefaults(projectId, now);
        settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns(row);
        var handler = new GetProjectSettingsHandler(settings);

        var view = await handler.HandleAsync(projectId, TestContext.Current.CancellationToken);

        view.ProjectId.ShouldBe(projectId);
        view.Version.ShouldBe(1);
    }

    [Fact(DisplayName = "Given matching version, when UpdateSettings runs, then Apply + SaveAsync run")]
    public async Task UpdateSettingsPersistsAsync()
    {
        var projectId = ProjectId.New();
        var row = ProjectSettings.CreateDefaults(projectId, now);
        settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns(row);
        settings.SaveAsync(row, Arg.Any<CancellationToken>()).Returns(static callInfo => callInfo.Arg<ProjectSettings>());
        var handler = new UpdateSettingsHandler(settings, clock);

        var view = await handler.HandleAsync(
            new UpdateSettingsCommand(projectId, 1, 1, 8, 60, true, true, true, true, 1000, 2000),
            TestContext.Current.CancellationToken);

        view.MinIdle.ShouldBe(1);
        view.MaxConcurrent.ShouldBe(8);
        view.ApproveRequired.ShouldBeTrue();
        view.SoftBudgetUsdMicros.ShouldBe(1000);
        view.HardBudgetUsdMicros.ShouldBe(2000);
        view.Version.ShouldBe(2);
        await settings.Received(1).SaveAsync(row, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a stale version, when UpdateSettings runs, then ProjectSettingsConflictException is thrown")]
    public async Task UpdateSettingsRefusesStaleVersionAsync()
    {
        var projectId = ProjectId.New();
        var row = ProjectSettings.CreateDefaults(projectId, now);
        settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns(row);
        var handler = new UpdateSettingsHandler(settings, clock);

        var exception = await Should.ThrowAsync<ProjectSettingsConflictException>(
            () => handler.HandleAsync(
                new UpdateSettingsCommand(projectId, 0, 0, 4, null, false, false, false, false, null, null),
                TestContext.Current.CancellationToken));

        exception.ProjectId.ShouldBe(projectId);
        exception.ExpectedVersion.ShouldBe(0);
        exception.CurrentVersion.ShouldBe(1);
    }

    [Fact(DisplayName = "Given missing settings, when GetSettings runs, then ProjectNotFoundException is thrown")]
    public async Task GetSettingsThrowsWhenMissingAsync()
    {
        var projectId = ProjectId.New();
        settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns((ProjectSettings?)null);
        var handler = new GetProjectSettingsHandler(settings);

        await Should.ThrowAsync<ProjectNotFoundException>(
            () => handler.HandleAsync(projectId, TestContext.Current.CancellationToken));
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
