using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Artifacts;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
using Comuki.Modules.Artifacts.Infrastructure.Store;
using Comuki.Modules.Identity.Infrastructure.Persistence;
using Comuki.Modules.Projects.Infrastructure.Persistence;
using Comuki.Shared.Kernel.Ids;
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

namespace Comuki.Host.Integration.Artifacts;

/// <summary>
/// Boots the real host composition on a random loopback port against
/// one migrated Testcontainers Postgres (all module schemas) plus a
/// Testcontainers MinIO with <see cref="ArtifactsOptions.AutoCreateBucket"/>
/// on, drives the artifact packager to terminal a seeded run and
/// asserts the API surfaces the bundle pointers.
/// </summary>
public sealed class ArtifactsEndToEndShould : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";
    private const string MinioUser = "comuki";
    private const string MinioPassword = "comuki_dev";
    private const string TestBucket = "comuki-test-bundles";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

#pragma warning disable CS0612
    private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest")
        .WithUsername(MinioUser)
        .WithPassword(MinioPassword)
        .Build();
#pragma warning restore CS0612

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private WebApplication application = null!;
    private Uri baseAddress = null!;
    private string connectionString = string.Empty;
    private string minioEndpoint = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.WhenAll(postgres.StartAsync(cancellationToken), minio.StartAsync(cancellationToken));

        connectionString = postgres.GetConnectionString();
        minioEndpoint = minio.GetConnectionString();

        // Migrate every module context the host composes.
        await MigrateAsync<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, cancellationToken);
        await MigrateAsync<ArtifactsDbContext>(ArtifactsDbContext.ApplyOptions, cancellationToken);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { ApplicationName = typeof(HostComposer).Assembly.GetName().Name });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;

        // The MinIO endpoint Testcontainers returns is host:port without
        // a scheme; the artifacts options expect host:port. The SDK
        // infers http (AutoCreateBucket) because UseSSL is off.
        var (host, port) = SplitEndpoint(minioEndpoint);
        builder.Configuration["Artifacts:Endpoint"] = $"{host}:{port}";
        builder.Configuration["Artifacts:AccessKey"] = MinioUser;
        builder.Configuration["Artifacts:SecretKey"] = MinioPassword;
        builder.Configuration["Artifacts:Bucket"] = TestBucket;
        builder.Configuration["Artifacts:UseSSL"] = "false";
        builder.Configuration["Artifacts:AutoCreateBucket"] = "true";

        // Program wires orchestration persistence before Compose.
        builder.Services.AddOrchestrationPersistence(connectionString);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        await application.StartAsync(cancellationToken);

        baseAddress = new Uri(
            application.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
                .Addresses.Single());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await application.DisposeAsync();
        await Task.WhenAll(postgres.DisposeAsync().AsTask(), minio.DisposeAsync().AsTask());
    }

    [Fact(DisplayName = "Given a terminal run, when the packager polls, then the bundle appears in MinIO and the API returns the pointers")]
    public async Task PackagerBundlesTerminalRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Seed a run that is already terminal (the only status the
        // packager acts on). The run is born Queued in the engine
        // domain; TransitionTo enforces the legal sequence so we walk
        // it through Running then Succeeded.
        var (projectId, runId) = await SeedTerminalSucceededRunAsync(cancellationToken);

        // Force the host packager to run one cycle immediately so the
        // test does not depend on the 10-second poll timer.
        await using var scope = application.Services.CreateAsyncScope();
        var hostDriver = scope.ServiceProvider
            .GetServices<IHostedService>()
            .OfType<RunArtifactPackagerHostService>()
            .Single();
        await hostDriver.PollOnceAsync(cancellationToken);

        // Log in as the bootstrap admin and call the artifacts endpoint.
        using var client = await CreateAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId.Value}/runs/{runId.Value}/artifacts",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ArtifactsPage>(cancellationToken);
        page.ShouldNotBeNull();
        page.Items.ShouldNotBeEmpty();

        var names = page.Items.Select(static p => p.Name).ToList();
        names.ShouldContain("brief.json");
        names.ShouldContain("result.json");
        names.ShouldContain("pins.json");

        var allPointers = page.Items;
        allPointers.ShouldAllBe(static p => !string.IsNullOrWhiteSpace(p.Name));
        allPointers.ShouldAllBe(static p => p.Uri.Scheme == "http" || p.Uri.Scheme == "https");
    }

    [Fact(DisplayName = "Given an in-flight run, when the packager polls, then the API returns no artifacts")]
    public async Task InFlightRunHasNoArtifactsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (projectId, runId) = await SeedInFlightRunAsync(cancellationToken);

        await using var scope = application.Services.CreateAsyncScope();
        var hostDriver = scope.ServiceProvider
            .GetServices<IHostedService>()
            .OfType<RunArtifactPackagerHostService>()
            .Single();
        await hostDriver.PollOnceAsync(cancellationToken);

        using var client = await CreateAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId.Value}/runs/{runId.Value}/artifacts",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ArtifactsPage>(cancellationToken);
        page.ShouldNotBeNull();
        page.Items.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a no-auth request, when the artifacts endpoint is called, then 401 is returned")]
    public async Task RefuseAnonymousAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (projectId, runId) = await SeedTerminalSucceededRunAsync(cancellationToken);

        using var client = new HttpClient { BaseAddress = baseAddress };
        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId.Value}/runs/{runId.Value}/artifacts",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(ProjectId ProjectId, RunId RunId)> SeedTerminalSucceededRunAsync(CancellationToken cancellationToken)
    {
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);

        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var run = Run.Create(projectId, now);
        run.TransitionTo(RunStatus.Running, now + TimeSpan.FromMinutes(1));
        run.TransitionTo(RunStatus.Succeeded, now + TimeSpan.FromMinutes(5));
        orchestrationDb.Runs.Add(run);

        // Seed a single work item so the packager can write brief.json
        // and the journal event payload has an originWorkItemId.
        var workItem = Engine.Orchestration.Domain.WorkItems.WorkItem.Create(
            runId,
            "implementer",
            "image:latest",
            "refs/heads/main",
                                 /*lang=json,strict*/
                                 """{"goal":"build a thing"}""",
            WorkItemStatus.Queued,
            now);
        orchestrationDb.WorkItems.Add(workItem);

        await orchestrationDb.SaveChangesAsync(cancellationToken);
        return (projectId, runId);
    }

    private async Task<(ProjectId ProjectId, RunId RunId)> SeedInFlightRunAsync(CancellationToken cancellationToken)
    {
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, connectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);

        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.UtcNow;

        var run = Run.Create(projectId, now);
        run.TransitionTo(RunStatus.Running, now + TimeSpan.FromMinutes(1));
        orchestrationDb.Runs.Add(run);
        await orchestrationDb.SaveChangesAsync(cancellationToken);
        return (projectId, runId);
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = BootstrapEmail, password = BootstrapPassword },
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return client;
    }

    private async Task MigrateAsync<TContext>(
        Action<DbContextOptionsBuilder, string> applyOptions,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>();
        applyOptions(options, connectionString);
        var constructor = typeof(TContext).GetConstructors().OrderByDescending(static ctor => ctor.GetParameters().Length).First();
        object?[] arguments = constructor.GetParameters().Length switch
        {
            1 => [options.Options],
            2 => [options.Options, null],
            _ => throw new InvalidOperationException("unexpected ctor arity on " + typeof(TContext).Name),
        };
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

    private sealed record ArtifactsPage(
        IReadOnlyList<PointerView> Items,
        Guid ProjectId,
        Guid RunId);

    private sealed record PointerView(
        string Name,
        Uri Uri,
        long SizeBytes,
        string ContentType);
}
