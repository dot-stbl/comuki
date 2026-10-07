using Comuki.Modules.Projects.Application.Attachments;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Application handlers over mocked ports: attach (success, missing
/// project, duplicate pair), detach (success, missing pair), list-by-project,
/// list-by-repository. Every port method invoked is stubbed explicitly so
/// NSubstitute does not return a default <c>null</c> that NREs deep in the
/// handler.
/// </summary>
public sealed class AttachDetachHandlersShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a free project and a free (project, repository) pair, when Attach runs, then the attachment is persisted and a view is returned")]
    public async Task AttachPersistsAsync()
    {
        var projects = Substitute.For<IProjectStore>();
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var mapper = Substitute.For<IProjectsMapper>();
        var projectId = ProjectId.New();
        var repositoryId = RepositoryId.New();
        projects.FindByIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(Project.Create("Acme", "acme", null, null, null, now));
        attachments.FindAsync(projectId, repositoryId, Arg.Any<CancellationToken>())
            .Returns((ProjectRepositoryAttachment?)null);
        var clock = new FakeTime(now);
        // The mapper is a stateless projection; the handler invokes
        // mapper.ToView(attachment) and returns whatever the mapper
        // produces. We stub a return value so NSubstitute doesn't NRE.
        mapper.ToView(Arg.Any<ProjectRepositoryAttachment>())
            .Returns(callInfo => new ProjectRepositoryAttachmentView
            {
                Id = callInfo.Arg<ProjectRepositoryAttachment>().Id,
                ProjectId = callInfo.Arg<ProjectRepositoryAttachment>().ProjectId,
                RepositoryId = callInfo.Arg<ProjectRepositoryAttachment>().RepositoryId,
                Role = callInfo.Arg<ProjectRepositoryAttachment>().Role.Value,
                Access = callInfo.Arg<ProjectRepositoryAttachment>().Access,
                CredentialOverrideRef = callInfo.Arg<ProjectRepositoryAttachment>().CredentialOverrideRef,
                CreatedAt = callInfo.Arg<ProjectRepositoryAttachment>().CreatedAt,
                UpdatedAt = callInfo.Arg<ProjectRepositoryAttachment>().UpdatedAt,
            });
        var handler = new AttachRepositoryHandler(attachments, projects, mapper, clock);

        var view = await handler.HandleAsync(
            new AttachRepositoryCommand(projectId, repositoryId, " Primary ", AttachmentAccess.Write, "integration-a"),
            TestContext.Current.CancellationToken);

        view.Role.ShouldBe("primary");
        view.Access.ShouldBe(AttachmentAccess.Write);
        view.CredentialOverrideRef.ShouldBe("integration-a");
        await attachments.Received(1).AddAsync(
            Arg.Is<ProjectRepositoryAttachment>(a =>
                a.ProjectId == projectId
                && a.RepositoryId == repositoryId
                && a.Role == AttachmentRole.Primary
                && a.Access == AttachmentAccess.Write
                && a.CredentialOverrideRef == "integration-a"),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a missing project, when Attach runs, then ProjectNotFoundException is thrown and nothing is persisted")]
    public async Task AttachRefusesMissingProjectAsync()
    {
        var projects = Substitute.For<IProjectStore>();
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var mapper = Substitute.For<IProjectsMapper>();
        var projectId = ProjectId.New();
        projects.FindByIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns((Project?)null);
        var handler = new AttachRepositoryHandler(attachments, projects, mapper, new FakeTime(now));

        await Should.ThrowAsync<ProjectNotFoundException>(
            () => handler.HandleAsync(
                new AttachRepositoryCommand(projectId, RepositoryId.New(), "primary", AttachmentAccess.Write, null),
                TestContext.Current.CancellationToken));

        await attachments.DidNotReceive().AddAsync(Arg.Any<ProjectRepositoryAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an existing (project, repository) attachment, when Attach runs again, then ProjectRepositoryAttachmentConflictException is thrown")]
    public async Task AttachRefusesDuplicateAsync()
    {
        var projects = Substitute.For<IProjectStore>();
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var mapper = Substitute.For<IProjectsMapper>();
        var projectId = ProjectId.New();
        var repositoryId = RepositoryId.New();
        projects.FindByIdAsync(projectId, Arg.Any<CancellationToken>())
            .Returns(Project.Create("Acme", "acme", null, null, null, now));
        attachments.FindAsync(projectId, repositoryId, Arg.Any<CancellationToken>())
            .Returns(ProjectRepositoryAttachment.Create(projectId, repositoryId, "primary", AttachmentAccess.Write, null, now));
        var handler = new AttachRepositoryHandler(attachments, projects, mapper, new FakeTime(now));

        var exception = await Should.ThrowAsync<ProjectRepositoryAttachmentConflictException>(
            () => handler.HandleAsync(
                new AttachRepositoryCommand(projectId, repositoryId, "library", AttachmentAccess.Read, null),
                TestContext.Current.CancellationToken));

        exception.ProjectId.ShouldBe(projectId);
        exception.RepositoryId.ShouldBe(repositoryId);
        await attachments.DidNotReceive().AddAsync(Arg.Any<ProjectRepositoryAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an existing (project, repository) attachment, when Detach runs, then the store's DeleteAsync is invoked")]
    public async Task DetachRemovesExistingAsync()
    {
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var projectId = ProjectId.New();
        var repositoryId = RepositoryId.New();
        attachments.DeleteAsync(projectId, repositoryId, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new DetachRepositoryHandler(attachments);

        await handler.HandleAsync(new DetachRepositoryCommand(projectId, repositoryId), TestContext.Current.CancellationToken);

        await attachments.Received(1).DeleteAsync(projectId, repositoryId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a missing (project, repository) pair, when Detach runs, then ProjectRepositoryAttachmentNotFoundException is thrown")]
    public async Task DetachRefusesMissingAsync()
    {
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var projectId = ProjectId.New();
        var repositoryId = RepositoryId.New();
        attachments.DeleteAsync(projectId, repositoryId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new DetachRepositoryHandler(attachments);

        var exception = await Should.ThrowAsync<ProjectRepositoryAttachmentNotFoundException>(
            () => handler.HandleAsync(new DetachRepositoryCommand(projectId, repositoryId), TestContext.Current.CancellationToken));

        exception.ProjectId.ShouldBe(projectId);
        exception.RepositoryId.ShouldBe(repositoryId);
    }

    [Fact(DisplayName = "Given attachments of one Project, when ListByProject runs, then the views are mapped from the port")]
    public async Task ListByProjectMapsViewsAsync()
    {
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var mapper = Substitute.For<IProjectsMapper>();
        var projectId = ProjectId.New();
        var first = ProjectRepositoryAttachment.Create(projectId, RepositoryId.New(), "primary", AttachmentAccess.Write, null, now);
        var second = ProjectRepositoryAttachment.Create(projectId, RepositoryId.New(), "library", AttachmentAccess.Read, null, now);
        attachments.ListByProjectAsync(projectId, Arg.Any<CancellationToken>()).Returns([first, second]);
        // The mapper is a stateless projection; the handler invokes
        // mapper.ToView(attachment) for each row and we round-trip the
        // role so the assertions below stay meaningful.
        mapper.ToView(Arg.Any<ProjectRepositoryAttachment>())
            .Returns(static callInfo => new ProjectRepositoryAttachmentView
            {
                Id = callInfo.Arg<ProjectRepositoryAttachment>().Id,
                ProjectId = callInfo.Arg<ProjectRepositoryAttachment>().ProjectId,
                RepositoryId = callInfo.Arg<ProjectRepositoryAttachment>().RepositoryId,
                Role = callInfo.Arg<ProjectRepositoryAttachment>().Role.Value,
                Access = callInfo.Arg<ProjectRepositoryAttachment>().Access,
                CredentialOverrideRef = callInfo.Arg<ProjectRepositoryAttachment>().CredentialOverrideRef,
                CreatedAt = callInfo.Arg<ProjectRepositoryAttachment>().CreatedAt,
                UpdatedAt = callInfo.Arg<ProjectRepositoryAttachment>().UpdatedAt,
            });
        var handler = new ListProjectAttachmentsHandler(attachments, mapper);

        var views = await handler.HandleAsync(projectId, TestContext.Current.CancellationToken);

        views.Count.ShouldBe(2);
        views[0].Role.ShouldBe("primary");
        views[1].Role.ShouldBe("library");
    }

    [Fact(DisplayName = "Given attachments pointing at one Repository, when ListByRepository runs, then the views are mapped from the port")]
    public async Task ListByRepositoryMapsViewsAsync()
    {
        var attachments = Substitute.For<IProjectRepositoryAttachmentStore>();
        var mapper = Substitute.For<IProjectsMapper>();
        var repositoryId = RepositoryId.New();
        var first = ProjectRepositoryAttachment.Create(ProjectId.New(), repositoryId, "primary", AttachmentAccess.Write, null, now);
        var second = ProjectRepositoryAttachment.Create(ProjectId.New(), repositoryId, "library", AttachmentAccess.Read, null, now);
        attachments.ListByRepositoryAsync(repositoryId, Arg.Any<CancellationToken>()).Returns([first, second]);
        mapper.ToView(Arg.Any<ProjectRepositoryAttachment>())
            .Returns(static callInfo => new ProjectRepositoryAttachmentView
            {
                Id = callInfo.Arg<ProjectRepositoryAttachment>().Id,
                ProjectId = callInfo.Arg<ProjectRepositoryAttachment>().ProjectId,
                RepositoryId = callInfo.Arg<ProjectRepositoryAttachment>().RepositoryId,
                Role = callInfo.Arg<ProjectRepositoryAttachment>().Role.Value,
                Access = callInfo.Arg<ProjectRepositoryAttachment>().Access,
                CredentialOverrideRef = callInfo.Arg<ProjectRepositoryAttachment>().CredentialOverrideRef,
                CreatedAt = callInfo.Arg<ProjectRepositoryAttachment>().CreatedAt,
                UpdatedAt = callInfo.Arg<ProjectRepositoryAttachment>().UpdatedAt,
            });
        var handler = new ListRepositoryAttachmentsHandler(attachments, mapper);

        var views = await handler.HandleAsync(repositoryId, TestContext.Current.CancellationToken);

        views.Count.ShouldBe(2);
        views[0].Access.ShouldBe(AttachmentAccess.Write);
        views[1].Access.ShouldBe(AttachmentAccess.Read);
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
