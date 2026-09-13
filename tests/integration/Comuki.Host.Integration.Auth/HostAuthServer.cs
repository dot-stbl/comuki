using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Auth;
using Comuki.Host.Testing;
using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Application.Views;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Modules.Identity.Domain.Users;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against a migrated Testcontainers Postgres (every
/// module context, via <see cref="HostDatabaseMigrator"/>), a temp
/// control-plane root, a configured bootstrap admin. One browser-like
/// client carries the cookie session; the client from <see cref="CreateApiKeyClient"/> is
/// cookie-less for bearer flows.
/// </summary>
public sealed class HostAuthServer : IAsyncLifetime
{
    public const string BootstrapEmail = TestBootstrapAdmin.Email;
    public const string BootstrapPassword = TestBootstrapAdmin.Password;

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private WebApplication application = null!;
    private TempControlPlaneRoot controlPlane = null!;
    private Uri baseAddress = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var connectionString = container.GetConnectionString();
        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        controlPlane = new TempControlPlaneRoot("auth");
        controlPlane.Write("profiles", "implement.md", """
            ---
            name: implement
            description: Implementation worker.
            allowedTools: [Read, Write]
            ---

            Body.
            """);
        controlPlane.WriteDefaultChatCommand();

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        TestBootstrapAdmin.Configure(builder.Configuration);
        // Lift the login bucket for the integration run — the test
        // suite logs in (bootstrap + per-test users) more than the
        // 10/min default. The rate-limit partition stays registered;
        // a high value makes it effectively a no-op.
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";
        TestArtifactsSecrets.ApplyPlaceholder(builder.Configuration);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        baseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
    }

    /// <summary>Cookie-carrying client (login sessions); per test, so session state never leaks between tests.</summary>
    public HttpClient CreateBrowserClient()
    {
        return new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
    }

    /// <summary>Cookie-less client (bearer API keys, anonymous calls); per test.</summary>
    public HttpClient CreateApiKeyClient()
    {
        return new HttpClient { BaseAddress = baseAddress };
    }

    /// <summary>Looks a user account up by email.</summary>
    public async Task<User?> FindUserAsync(string email)
    {
        using var scope = application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUserAccountStore>()
            .FindByEmailAsync(email, TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a local account directly through the handler.</summary>
    public async Task<UserAccountView> CreateUserAsync(string email, string password = "user-pass-123")
    {
        using var scope = application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<CreateUserHandler>()
            .HandleAsync(new CreateUserCommand(email, email, password), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Grants a role to a subject at platform scope through the handler —
    /// as a system consumer, because the handler's duplicate guard reads
    /// <c>RoleAssignments</c> (query-filtered) before this call ever
    /// reaches an authenticated request path.
    /// </summary>
    public async Task GrantPlatformRoleAsync(RoleSubject subject, Role role)
    {
        using var scope = application.Services.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("test-seeder");

        await scope.ServiceProvider.GetRequiredService<GrantRoleHandler>()
            .HandleAsync(
                new GrantRoleCommand(subject, role, AssignmentScope.Platform(), ActingAs: null),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Grants a role to a subject on exactly one project through the
    /// handler — as a system consumer, same rationale as
    /// <see cref="GrantPlatformRoleAsync"/>.
    /// </summary>
    public async Task GrantProjectRoleAsync(RoleSubject subject, Role role, ProjectId projectId)
    {
        using var scope = application.Services.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("test-seeder");

        await scope.ServiceProvider.GetRequiredService<GrantRoleHandler>()
            .HandleAsync(
                new GrantRoleCommand(subject, role, AssignmentScope.ForProject(projectId), ActingAs: null),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Creates a project directly through the handler — as a system
    /// consumer, because a seeding flow owns no subject.
    /// </summary>
    public async Task<ProjectView> CreateProjectAsync(string name, string slug)
    {
        using var scope = application.Services.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("test-seeder");

        return await scope.ServiceProvider.GetRequiredService<CreateProjectHandler>()
            .HandleAsync(
                new CreateProjectCommand(name, slug, null, null, null),
                TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds one run with a single queued work item — as a system
    /// consumer, because a seeding flow owns no subject.
    /// </summary>
    public async Task<RunId> SeedRunWithItemAsync(ProjectId projectId)
    {
        using var scope = application.Services.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("test-seeder");

        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var run = Run.Create(projectId, now);
        var item = WorkItem.Create(
            run.Id,
            "implement",
            "ghcr.io/comuki/worker:test",
            "refs/heads/main",
            /*lang=json,strict*/ """{"goal":"scope check"}""",
            WorkItemStatus.Queued,
            now);

        db.Runs.Add(run);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return run.Id;
    }

    /// <summary>
    /// The run ids a subject's scope lets it see, through the host's own
    /// accessor + context (the query filter in force, not a re-implementation).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> VisibleRunsAsync(RoleSubject subject)
    {
        var accessor = application.Services.GetRequiredService<ISubjectScopeAccessor>();
        SubjectAuthorization authorization;
        using (accessor.AsSystem("permission-eval"))
        {
            authorization = await application.Services.GetRequiredService<IPermissionEvaluator>()
                .EvaluateAsync(subject, TestContext.Current.CancellationToken);
        }

        using var scope = application.Services.CreateScope();
        using (accessor.Begin(authorization.ToSubjectScope()))
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
            var runs = await db.Runs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

            return [.. runs.Select(static run => run.Id.Value)];
        }
    }

    /// <summary>
    /// The work-item ids a subject's scope lets it see, through the host's
    /// own accessor + context (work items filter through their parent run).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> VisibleWorkItemsAsync(RoleSubject subject)
    {
        var accessor = application.Services.GetRequiredService<ISubjectScopeAccessor>();
        SubjectAuthorization authorization;
        using (accessor.AsSystem("permission-eval"))
        {
            authorization = await application.Services.GetRequiredService<IPermissionEvaluator>()
                .EvaluateAsync(subject, TestContext.Current.CancellationToken);
        }

        using var scope = application.Services.CreateScope();
        using (accessor.Begin(authorization.ToSubjectScope()))
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();
            var items = await db.WorkItems.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

            return [.. items.Select(static item => item.Id)];
        }
    }

    /// <summary>Issues an API key for an owner through the module issuer.</summary>
    public async Task<IssuedApiKeyCredential> IssueApiKeyAsync(UserId ownerId)
    {
        using var scope = application.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ApiKeyIssuer>()
            .IssueAsync(ownerId, "integration-key", null, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs the bootstrap seeder one more time (idempotence probe).</summary>
    public async Task RunBootstrapSeederAgainAsync()
    {
        using var scope = application.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<BootstrapAdminSeeder>()
            .SeedAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Lists the active platform assignments of a subject — as a system
    /// consumer, because <c>RoleAssignments</c> is query-filtered and this
    /// read never rides an authenticated request path.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActiveRoleKeysAsync(RoleSubject subject)
    {
        using var scope = application.Services.CreateScope();
        using var systemScope = scope.ServiceProvider
            .GetRequiredService<ISubjectScopeAccessor>()
            .AsSystem("test-seeder");

        var assignments = await scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>()
            .ListActiveAsync(subject, TestContext.Current.CancellationToken);

        return [.. assignments.Select(static assignment => RoleKeys.Key(assignment.Role))];
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
        await container.DisposeAsync();
    }
}
