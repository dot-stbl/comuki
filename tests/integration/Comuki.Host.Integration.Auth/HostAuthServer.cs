using System.Net;
using System.Net.Sockets;
using System.Text;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Auth;
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
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Application.Projects.Create;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Auth;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against a migrated Testcontainers Postgres:
/// both module contexts migrated, a temp control-plane root, a
/// configured bootstrap admin. One browser-like client carries the
/// cookie session; <see cref="ApiKeyClient"/> is cookie-less for
/// bearer flows.
/// </summary>
public sealed class HostAuthServer : IAsyncLifetime
{
    public const string BootstrapEmail = "bootstrap@comuki.test";
    public const string BootstrapPassword = "bootstrap-pass-1";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
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

        // The migrator's contract: both module contexts migrate the same
        // database, each with its own migrations history table.
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);
        await orchestrationDb.Database.MigrateAsync(cancellationToken);

        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.ApplyOptions(identityOptions, connectionString);
        await using var identityDb = new IdentityDbContext(identityOptions.Options);
        await identityDb.Database.MigrateAsync(cancellationToken);

        var projectsOptions = new DbContextOptionsBuilder<ProjectsDbContext>();
        ProjectsDbContext.ApplyOptions(projectsOptions, connectionString);
        await using var projectsDb = new ProjectsDbContext(projectsOptions.Options);
        await projectsDb.Database.MigrateAsync(cancellationToken);

        controlPlane = new TempControlPlaneRoot();
        controlPlane.WriteProfile();
        controlPlane.WriteChatCommand();

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
                // Production env on purpose: Development turns on
                // ValidateScopes and the intake installers currently
                // register handlers as singletons over a scoped DbContext
                // (pre-existing; HostChatServer does the same). The
                // production-secret validator (issue #10 T11.4) is
                // satisfied with non-dev-default secrets below.
                EnvironmentName = Environments.Production,
            });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["ControlPlane:Root"] = controlPlane.Root;
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;
        // Lift the login bucket for the integration run — the test
        // suite logs in (bootstrap + per-test users) more than the
        // 10/min default. The rate-limit partition stays registered;
        // a high value makes it effectively a no-op.
        builder.Configuration["Host:RateLimit:LoginPermitsPerMinute"] = "10000";
        // Artifacts module — non-dev-default secrets so the
        // ProductionSecretValidator (issue #10 T11.4) passes through.
        builder.Configuration["Artifacts:Endpoint"] = "minio:9000";
        builder.Configuration["Artifacts:AccessKey"] = "test-access-key";
        builder.Configuration["Artifacts:SecretKey"] = "test-secret-key-with-enough-entropy";
        builder.Configuration["Artifacts:Bucket"] = "comuki-test-bundles";

        // Program wires orchestration persistence before Compose (the worker
        // runtime and the scoped reads below resolve the context); the scope
        // fixture's run seeds and visibility probes need it too.
        _ = builder.Services.AddOrchestrationPersistence(connectionString);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        await application.StartAsync(cancellationToken);

        baseAddress = new Uri(
            application.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single());
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

    /// <summary>Grants a role to a subject at platform scope through the handler.</summary>
    public async Task GrantPlatformRoleAsync(RoleSubject subject, Role role)
    {
        using var scope = application.Services.CreateScope();

        _ = await scope.ServiceProvider.GetRequiredService<GrantRoleHandler>()
            .HandleAsync(
                new GrantRoleCommand(subject, role, AssignmentScope.Platform(), ActingAs: null),
                TestContext.Current.CancellationToken);
    }

    /// <summary>Grants a role to a subject on exactly one project through the handler.</summary>
    public async Task GrantProjectRoleAsync(RoleSubject subject, Role role, ProjectId projectId)
    {
        using var scope = application.Services.CreateScope();

        _ = await scope.ServiceProvider.GetRequiredService<GrantRoleHandler>()
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

        _ = db.Runs.Add(run);
        _ = db.WorkItems.Add(item);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return run.Id;
    }

    /// <summary>
    /// The run ids a subject's scope lets it see, through the host's own
    /// accessor + context (the query filter in force, not a re-implementation).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> VisibleRunsAsync(RoleSubject subject)
    {
        var accessor = application.Services.GetRequiredService<ISubjectScopeAccessor>();
        var authorization = await application.Services.GetRequiredService<IPermissionEvaluator>()
            .EvaluateAsync(subject, TestContext.Current.CancellationToken);

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
        var authorization = await application.Services.GetRequiredService<IPermissionEvaluator>()
            .EvaluateAsync(subject, TestContext.Current.CancellationToken);

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
            .IssueAsync(ownerId, "integration-key", TestContext.Current.CancellationToken);
    }

    /// <summary>Runs the bootstrap seeder one more time (idempotence probe).</summary>
    public async Task RunBootstrapSeederAgainAsync()
    {
        using var scope = application.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<BootstrapAdminSeeder>()
            .SeedAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Lists the active platform assignments of a subject.</summary>
    public async Task<IReadOnlyList<string>> ActiveRoleKeysAsync(RoleSubject subject)
    {
        using var scope = application.Services.CreateScope();
        var assignments = await scope.ServiceProvider.GetRequiredService<IRoleAssignmentStore>()
            .ListActiveAsync(subject, TestContext.Current.CancellationToken);

        return [.. assignments.Select(static assignment => RoleKeys.Key(assignment.Role))];
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        controlPlane.Dispose();
        await container.DisposeAsync();
    }
}

/// <summary>Throwaway control-plane root with one profile and one chat command.</summary>
internal sealed class TempControlPlaneRoot : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "comuki-host-auth-" + Guid.NewGuid().ToString("N"));

    public void WriteProfile()
    {
        Write("profiles", "implement.md", """
            ---
            name: implement
            description: Implementation worker.
            allowedTools: [Read, Write]
            ---

            Body.
            """);
    }

    public void WriteChatCommand()
    {
        Write("chat-commands", "restart.md", """
            ---
            name: restart
            description: Restart the current run.
            ---

            Body.
            """);
    }

    public void Write(string folderName, string fileName, string content)
    {
        var directory = Path.Combine(Root, folderName);
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
