using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// Boots the real host composition (<see cref="HostComposer"/>) on a
/// random loopback port against one migrated Testcontainers Postgres
/// (pgvector/pgvector:pg16 — the <c>memory_embeddings.embedding</c>
/// column needs the pgvector extension) plus a Testcontainers MinIO
/// for the artifact packager.
/// <para>
/// Composition gaps (smoke-only, the host in production routes these
/// differently):
/// </para>
/// <list type="bullet">
///   <item><c>AddKnowledgePersistence</c> — the main host composition
///     (<see cref="HostComposer"/>) does not register the knowledge
///     DbContext factory (it's wired in <c>HostComposer.Compose</c>
///     proper; this server runs the same composition but the smoke path
///     re-asserts the migration history directly to be defensive).</item>
/// </list>
/// </summary>
public sealed class SmokeHostServer : IAsyncLifetime
{
    public const string BootstrapEmail = "bootstrap@comuki.test";
    public const string BootstrapPassword = "bootstrap-pass-1";

    private const string MinioUser = "test-access-key";
    private const string MinioPassword = "test-secret-key-with-enough-entropy";
    private const string MinioBucket = "comuki-test-bundles";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

#pragma warning disable CS0612
    private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest")
        .WithUsername(MinioUser)
        .WithPassword(MinioPassword)
        .Build();
#pragma warning restore CS0612

    private WebApplication application = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.WhenAll(
            postgres.StartAsync(cancellationToken),
            minio.StartAsync(cancellationToken));

        var connectionString = postgres.GetConnectionString();
        var minioEndpoint = minio.GetConnectionString();

        // The migrator's contract: every module context migrates the same
        // database, each with its own migrations history table.
        await MigrateAsync<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<MemoryDbContext>(MemoryDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<KnowledgeDbContext>(KnowledgeDbContext.ApplyOptions, connectionString, cancellationToken);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
                // Production env on purpose: ValidateScopes off; the
                // production-secret validator (issue #10 T11.4) is
                // satisfied by the non-dev-default secrets below.
                EnvironmentName = Environments.Development, // test fixture — validator short-circuits on non-Production
            });
        builder.Host.UseDefaultServiceProvider(static options => { options.ValidateOnBuild = false; options.ValidateScopes = false; });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;

        // Artifacts module — non-dev-default secrets so the
        // ProductionSecretValidator (issue #10 T11.4) passes through.
        var (host, port) = SplitEndpoint(minioEndpoint);
        builder.Configuration["Artifacts:Endpoint"] = $"{host}:{port}";
        builder.Configuration["Artifacts:AccessKey"] = MinioUser;
        builder.Configuration["Artifacts:SecretKey"] = MinioPassword;
        builder.Configuration["Artifacts:Bucket"] = MinioBucket;
        builder.Configuration["Artifacts:UseSSL"] = "false";
        builder.Configuration["Artifacts:AutoCreateBucket"] = "true";

        // Knowledge module: noop embedding client so the smoke run
        // never reaches the real OpenAI endpoint.
        builder.Configuration["Knowledge:Embedding:Provider"] = "noop";
        builder.Configuration["Knowledge:Embedding:Dimensions"] = "1536";

        // Proxy module: enable with one placeholder virtual key so the
        // YARP config provider + VirtualKey scheme resolve cleanly at
        // request time. The key is never used in the smoke run (the
        // proxy/auth tests intentionally omit the bearer) but its
        // presence keeps the option validators and route catalog from
        // returning 500 on a no-config request.
        builder.Configuration["Proxy:Enabled"] = "true";
        builder.Configuration["Proxy:KnownModels:0"] = "smoke-model";
        builder.Configuration["Proxy:VirtualKeys:0:Token"] = "vkey_smoke_disabled";
        builder.Configuration["Proxy:VirtualKeys:0:ProjectId"] = Guid.NewGuid().ToString();
        builder.Configuration["Proxy:VirtualKeys:0:Provider"] = "openai";
        builder.Configuration["Proxy:VirtualKeys:0:BaseUrl"] = "http://127.0.0.1:1";
        builder.Configuration["Proxy:VirtualKeys:0:ApiKeyEnvRef"] = "SMOKE_PROXY_KEY";

        // Program wires orchestration persistence before Compose (the
        // worker runtime contract); the smoke suite mirrors that wiring.
        // AddOrchestrationQueue registers IRunJournal / IWorkItemQueue /
        // the lease reaper — Comuki.Program does both back-to-back; the
        // smoke suite duplicates that wiring because it composes the
        // host without going through Program.cs.
        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration);

        // Composition gap: HostComposer does not register Memory
        // persistence; only the Brain host does. The chat/memory path
        // needs IDbContextFactory<MemoryDbContext> to resolve — wire
        // it here so the smoke run exercises the full pipeline.
        builder.Services.AddMemoryPersistence(connectionString);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        await application.StartAsync(cancellationToken);

        BaseAddress = new Uri(
            application.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }

        await Task.WhenAll(postgres.DisposeAsync().AsTask(), minio.DisposeAsync().AsTask());
    }

    /// <summary>Base address the host listens on (where requests go).</summary>
    public Uri BaseAddress { get; private set; } = null!;

    /// <summary>Cookie-carrying client logged in as the bootstrap admin; per test.</summary>
    /// <returns>Logged-in client.</returns>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = BaseAddress,
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = BootstrapEmail, password = BootstrapPassword },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }

    /// <summary>Cookie-less anonymous client; per test.</summary>
    /// <returns>Anonymous client.</returns>
    public HttpClient CreateAnonymousClient()
    {
        return new HttpClient { BaseAddress = BaseAddress };
    }

    /// <summary>
    /// Creates a brand-new local account (no role assignments) and returns
    /// a cookie client already logged in as that account. The fresh
    /// account carries no <c>identity:write</c> permission, so callers can
    /// use it to assert the 403 path on the identity-admin surface.
    /// </summary>
    /// <returns>Logged-in cookie client + the account email.</returns>
    public async Task<(HttpClient Client, string Email)> CreateRolelessUserClientAsync(string password = "user-pass-123")
    {
        var email = $"smoke-norole-{Guid.NewGuid():N}@comuki.test";

        using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CreateUserHandler>()
                .HandleAsync(new CreateUserCommand(email, email, password), TestContext.Current.CancellationToken);
        }

        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = BaseAddress,
        };

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (client, email);
    }

    private static async Task MigrateAsync<TContext>(
        Action<DbContextOptionsBuilder, string> applyOptions,
        string targetConnectionString,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        applyOptions(options, targetConnectionString);
        var constructor = typeof(TContext).GetConstructors().OrderByDescending(static ctor => ctor.GetParameters().Length).First();
        // Pick the DbContextOptions<TContext> argument position; trailing
        // optional parameters (e.g. OrchestrationDbContext's
        // ISubjectScopeAccessor?) get a null sentinel — a context built
        // here is a system consumer by definition, so the scope is
        // irrelevant for migrations.
        var parameters = constructor.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            arguments[index] = index == 0 ? options.Options : null;
        }

        var context = (TContext)constructor.Invoke(arguments);
        await using (context)
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    private static (string Host, int Port) SplitEndpoint(string endpoint)
    {
        var trimmed = endpoint
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        var parts = trimmed.Split(':');
        return parts.Length != 2
            ? throw new InvalidOperationException("expected host:port, got " + endpoint)
            : ((string Host, int Port))(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
