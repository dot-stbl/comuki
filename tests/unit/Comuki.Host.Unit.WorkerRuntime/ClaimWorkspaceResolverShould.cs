using Comuki.Host.Workers.Workspace;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Claim workspace resolution (issue #125): the source git fields come
/// from the project row; the credential comes from the settings' secret
/// reference resolved server-side; an unresolvable reference degrades to
/// "no credential" (the unauthenticated clone fails at prepare — the
/// spec's failure point) instead of stranding the already-leased item.
/// </summary>
public sealed class ClaimWorkspaceResolverShould
{
    private readonly DateTimeOffset now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly IProjectStore projects = Substitute.For<IProjectStore>();
    private readonly IProjectSettingsStore settings = Substitute.For<IProjectSettingsStore>();
    private readonly ISecretResolver secrets = Substitute.For<ISecretResolver>();
    private readonly ClaimWorkspaceResolver resolver;

    public ClaimWorkspaceResolverShould()
    {
        resolver = new ClaimWorkspaceResolver(projects, settings, secrets, NullLogger<ClaimWorkspaceResolver>.Instance);
    }

    [Fact(DisplayName = "Given a project without a source git url, when resolved, then the workspace is null")]
    public async Task ReturnNullWithoutSourceGitUrlAsync()
    {
        var project = StoreProject(sourceGitUrl: null);

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a missing project row, when resolved, then the workspace is null")]
    public async Task ReturnNullWithoutProjectAsync()
    {
        var projectId = ProjectId.New();
        projects.FindByIdAsync(projectId, Arg.Any<CancellationToken>()).Returns((Project?)null);

        var workspace = await resolver.ResolveAsync(projectId.Value, TestContext.Current.CancellationToken);

        workspace.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a source url without a credential ref, when resolved, then the workspace carries the url and no credential")]
    public async Task ResolveWithoutCredentialAsync()
    {
        var project = StoreProject("https://git.example.com/acme/product.git");
        settings.FindAsync(project.Id, Arg.Any<CancellationToken>()).Returns(ProjectSettings.CreateDefaults(project.Id, now));

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldNotBeNull();
        workspace.SourceGitUrl.ShouldBe("https://git.example.com/acme/product.git");
        workspace.SourceGitRef.ShouldBeNull();
        workspace.GitCredential.ShouldBeNull();
        await secrets.DidNotReceive().ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a credential ref, when resolved, then the secret resolver supplies the value")]
    public async Task ResolveCredentialFromSettingsAsync()
    {
        var project = StoreProject("https://git.example.com/acme/product.git", "refs/tags/v2");
        settings.FindAsync(project.Id, Arg.Any<CancellationToken>()).Returns(SettingsWithCredential(project.Id, "env:ACME_GIT_TOKEN"));
        secrets.ResolveAsync("env:ACME_GIT_TOKEN", Arg.Any<CancellationToken>()).Returns("git_secret_token_123");

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldNotBeNull();
        workspace.SourceGitRef.ShouldBe("refs/tags/v2");
        workspace.GitCredential.ShouldBe("git_secret_token_123");
    }

    [Fact(DisplayName = "Given an unresolvable credential ref, when resolved, then the workspace degrades to no credential")]
    public async Task DegradeToNoCredentialWhenUnresolvableAsync()
    {
        var project = StoreProject("https://git.example.com/acme/product.git");
        settings.FindAsync(project.Id, Arg.Any<CancellationToken>()).Returns(SettingsWithCredential(project.Id, "env:UNSET_GIT_TOKEN"));
        secrets.ResolveAsync("env:UNSET_GIT_TOKEN", Arg.Any<CancellationToken>())
            .Returns<string?>(static _ => throw new SecretRefUnsetException("env:UNSET_GIT_TOKEN"));

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldNotBeNull();
        workspace.GitCredential.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a malformed credential ref, when resolved, then the workspace degrades to no credential")]
    public async Task DegradeToNoCredentialWhenMalformedAsync()
    {
        var project = StoreProject("https://git.example.com/acme/product.git");
        settings.FindAsync(project.Id, Arg.Any<CancellationToken>()).Returns(SettingsWithCredential(project.Id, "bogus-scheme:x"));
        secrets.ResolveAsync("bogus-scheme:x", Arg.Any<CancellationToken>())
            .Returns<string?>(static _ => throw new SecretRefFormatException("unknown secret ref scheme 'bogus-scheme'"));

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldNotBeNull();
        workspace.GitCredential.ShouldBeNull();
    }

    [Fact(DisplayName = "Given no settings row, when resolved, then the workspace carries the url and no credential")]
    public async Task ResolveWithoutSettingsRowAsync()
    {
        var project = StoreProject("https://git.example.com/acme/product.git");
        settings.FindAsync(project.Id, Arg.Any<CancellationToken>()).Returns((ProjectSettings?)null);

        var workspace = await resolver.ResolveAsync(project.Id.Value, TestContext.Current.CancellationToken);

        workspace.ShouldNotBeNull();
        workspace.GitCredential.ShouldBeNull();
    }

    private Project StoreProject(string? sourceGitUrl, string? sourceGitRef = null)
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, sourceGitUrl: sourceGitUrl, sourceGitRef: sourceGitRef);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        return project;
    }

    private ProjectSettings SettingsWithCredential(ProjectId projectId, string reference)
    {
        var row = ProjectSettings.CreateDefaults(projectId, now);
        row.Apply(0, 4, null, false, false, false, false, null, null, ProjectDomainType.Standard, null, reference, now);
        return row;
    }
}
