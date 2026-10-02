using Comuki.Host.Workers;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Source-git enrichment of a claim (harden-pi-worker-sandbox 4.3):
/// a missing <c>SourceGitUrl</c> yields all-null output (the worker
/// fails the item with "missing SourceGitUrl"); a project with a URL
/// yields URL + ref; a credential ref is resolved through the secret
/// resolver; a ref that fails to resolve logs a warning and yields a
/// null credential (no claim-time special case for "private without
/// credential" — that path lands on the worker through the natural
/// anonymous-clone failure).
/// </summary>
public sealed class ClaimSourceGitResolverShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly IProjectStore projects = Substitute.For<IProjectStore>();
    private readonly IProjectSettingsStore settings = Substitute.For<IProjectSettingsStore>();
    private readonly ISecretResolver secrets = Substitute.For<ISecretResolver>();

    [Fact(DisplayName = "Given a project with no SourceGitUrl, when ResolveAsync runs, then all three result fields are null")]
    public async Task NullUrlYieldsAllNullsAsync()
    {
        var projectId = Guid.NewGuid();
        var project = Project.Create("NoSource", "no-source", null, null, null, now);
        projects.FindByIdAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(project);
        var resolver = new ClaimSourceGitResolver(projects, settings, secrets, NullLogger<ClaimSourceGitResolver>.Instance);

        var result = await resolver.ResolveAsync(projectId, TestContext.Current.CancellationToken);

        result.SourceGitUrl.ShouldBeNull();
        result.SourceGitRef.ShouldBeNull();
        result.GitCredential.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a missing project, when ResolveAsync runs, then all three result fields are null")]
    public async Task MissingProjectYieldsAllNullsAsync()
    {
        var projectId = Guid.NewGuid();
        projects.FindByIdAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns((Project?)null);
        var resolver = new ClaimSourceGitResolver(projects, settings, secrets, NullLogger<ClaimSourceGitResolver>.Instance);

        var result = await resolver.ResolveAsync(projectId, TestContext.Current.CancellationToken);

        result.SourceGitUrl.ShouldBeNull();
        result.SourceGitRef.ShouldBeNull();
        result.GitCredential.ShouldBeNull();
        await settings.DidNotReceiveWithAnyArgs().FindAsync(default, TestContext.Current.CancellationToken);
        await secrets.DidNotReceiveWithAnyArgs().ResolveAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a project with URL + ref but no settings row, when ResolveAsync runs, then URL + ref are returned and credential is null")]
    public async Task UrlWithNoSettingsYieldsNullCredentialAsync()
    {
        var projectId = Guid.NewGuid();
        var project = Project.Create("HasSource", "has-source", null, null, null, now, sourceGitUrl: "https://github.com/example/repo.git", sourceGitRef: "main");
        projects.FindByIdAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(project);
        settings.FindAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns((ProjectSettings?)null);
        var resolver = new ClaimSourceGitResolver(projects, settings, secrets, NullLogger<ClaimSourceGitResolver>.Instance);

        var result = await resolver.ResolveAsync(projectId, TestContext.Current.CancellationToken);

        result.SourceGitUrl.ShouldBe("https://github.com/example/repo.git");
        result.SourceGitRef.ShouldBe("main");
        result.GitCredential.ShouldBeNull();
        await secrets.DidNotReceiveWithAnyArgs().ResolveAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given a project with URL + settings.GitCredentialRef env:TOKEN, when ResolveAsync runs, then the resolved token is the credential")]
    public async Task ResolvedCredentialFlowsToResultAsync()
    {
        var projectId = Guid.NewGuid();
        var project = Project.Create("HasSource", "has-source", null, null, null, now, sourceGitUrl: "https://github.com/example/repo.git", sourceGitRef: "main");
        var settingsRow = ProjectSettings.FromSnapshot(
            projectId: new ProjectId(projectId),
            minIdle: 0,
            maxConcurrent: ProjectSettings.DefaultMaxConcurrent,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: null,
            hardBudgetUsdMicros: null,
            domainType: ProjectDomainType.Standard,
            customDomainTypesJson: null,
            gitCredentialRef: "env:GH_TOKEN",
            updatedAt: now,
            version: 1);
        projects.FindByIdAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(project);
        settings.FindAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(settingsRow);
        secrets.ResolveAsync("env:GH_TOKEN", Arg.Any<CancellationToken>()).Returns("resolved-token-value");
        var resolver = new ClaimSourceGitResolver(projects, settings, secrets, NullLogger<ClaimSourceGitResolver>.Instance);

        var result = await resolver.ResolveAsync(projectId, TestContext.Current.CancellationToken);

        result.SourceGitUrl.ShouldBe("https://github.com/example/repo.git");
        result.SourceGitRef.ShouldBe("main");
        result.GitCredential.ShouldBe("resolved-token-value");
        await secrets.Received(1).ResolveAsync("env:GH_TOKEN", Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a project with settings.GitCredentialRef that the resolver rejects, when ResolveAsync runs, then credential is null and the call does not propagate")]
    public async Task UnresolvableCredentialRefYieldsNullWithoutThrowAsync()
    {
        var projectId = Guid.NewGuid();
        var project = Project.Create("HasSource", "has-source", null, null, null, now, sourceGitUrl: "https://github.com/example/repo.git", sourceGitRef: "main");
        var settingsRow = ProjectSettings.FromSnapshot(
            projectId: new ProjectId(projectId),
            minIdle: 0,
            maxConcurrent: ProjectSettings.DefaultMaxConcurrent,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: null,
            hardBudgetUsdMicros: null,
            domainType: ProjectDomainType.Standard,
            customDomainTypesJson: null,
            gitCredentialRef: "env:MISSING_TOKEN",
            updatedAt: now,
            version: 1);
        projects.FindByIdAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(project);
        settings.FindAsync(new ProjectId(projectId), Arg.Any<CancellationToken>()).Returns(settingsRow);
        secrets.ResolveAsync("env:MISSING_TOKEN", Arg.Any<CancellationToken>())
            .ThrowsAsync(new SecretRefUnsetException("env:MISSING_TOKEN"));
        var resolver = new ClaimSourceGitResolver(projects, settings, secrets, NullLogger<ClaimSourceGitResolver>.Instance);

        var result = await resolver.ResolveAsync(projectId, TestContext.Current.CancellationToken);

        result.SourceGitUrl.ShouldBe("https://github.com/example/repo.git");
        result.SourceGitRef.ShouldBe("main");
        result.GitCredential.ShouldBeNull();
    }
}
