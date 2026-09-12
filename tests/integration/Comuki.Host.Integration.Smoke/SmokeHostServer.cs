using System.Net;
using System.Net.Http.Json;
using Comuki.Host.Testing;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Memory.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
/// column needs the pgvector extension) — every module context, via
/// <see cref="HostDatabaseMigrator"/> — plus a Testcontainers MinIO for
/// the artifact packager.
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
    public const string BootstrapEmail = TestBootstrapAdmin.Email;
    public const string BootstrapPassword = TestBootstrapAdmin.Password;

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

#pragma warning disable CS0612
    private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest")
        .WithUsername(TestArtifactsSecrets.AccessKey)
        .WithPassword(TestArtifactsSecrets.SecretKey)
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

        await HostDatabaseMigrator.MigrateAllAsync(connectionString, cancellationToken);

        var builder = TestHostBuilder.Create(connectionString);
        builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);
        TestBootstrapAdmin.Configure(builder.Configuration);

        // Artifacts module — the real MinIO container this suite booted,
        // not the placeholder endpoint the other harnesses configure.
        var (host, port) = SplitEndpoint(minioEndpoint);
        builder.Configuration["Artifacts:Endpoint"] = $"{host}:{port}";
        builder.Configuration["Artifacts:AccessKey"] = TestArtifactsSecrets.AccessKey;
        builder.Configuration["Artifacts:SecretKey"] = TestArtifactsSecrets.SecretKey;
        builder.Configuration["Artifacts:Bucket"] = TestArtifactsSecrets.Bucket;
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

        // Composition gap: HostComposer does not register Memory
        // persistence; only the Brain host does. The chat/memory path
        // needs IDbContextFactory<MemoryDbContext> to resolve — wire
        // it here so the smoke run exercises the full pipeline.
        builder.Services.AddMemoryPersistence(connectionString);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        BaseAddress = await TestHostBuilder.StartAsync(application, cancellationToken);
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
    public Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = BaseAddress,
        };

        return client.LoginAsBootstrapAdminAsync(TestContext.Current.CancellationToken);
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
}
