using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Orchestration.Application;
using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Domain.WorkItems;
using Comuki.Engine.Orchestration.Infrastructure;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Host.Workers;
using Comuki.Modules.Artifacts.Infrastructure.Persistence;
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
/// End-to-end surface (issue #51 slice 1):
/// <list type="bullet">
///   <item>Worker uploads an <c>image/png</c> against a leased work
///   item — 204, the row lands in <c>artifacts.visual_artifacts</c>.</item>
///   <item>Anonymous GET on the content proxy — 401.</item>
///   <item>HTML response carries the strict CSP + <c>nosniff</c>.</item>
/// </list>
/// Boots the real host composition on a random loopback port against
/// one migrated Testcontainers Postgres (every module schema) plus a
/// Testcontainers MinIO with <see cref="ArtifactsOptions.AutoCreateBucket"/>
/// on, then drives the worker upload flow against a seeded run.
/// </summary>
public sealed class WorkerUploadArtifactShould : IAsyncLifetime
{
    private const string BootstrapEmail = "bootstrap@comuki.test";
    private const string BootstrapPassword = "bootstrap-pass-1";
    private const string MinioUser = "test-access-key";
    private const string MinioPassword = "test-secret-key-with-enough-entropy";
    private const string TestBucket = "comuki-test-bundles";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

#pragma warning disable CS0612
    private readonly MinioContainer minio = new MinioBuilder("minio/minio:latest")
        .WithUsername(MinioUser)
        .WithPassword(MinioPassword)
        .Build();
#pragma warning restore CS0612

    private WebApplication application = null!;
    private Uri baseAddress = null!;
    private string seedConnectionString = string.Empty;
    private string minioEndpoint = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.WhenAll(
            postgres.StartAsync(cancellationToken),
            minio.StartAsync(cancellationToken));

        var connectionString = postgres.GetConnectionString() + ";Application Name=host;Pooling=false";
        seedConnectionString = connectionString;
        minioEndpoint = minio.GetConnectionString();

        await MigrateAsync<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, connectionString, cancellationToken);
        await MigrateAsync<ArtifactsDbContext>(ArtifactsDbContext.ApplyOptions, connectionString, cancellationToken);

        await MigrateAsync<OrchestrationDbContext>(OrchestrationDbContext.ApplyOptions, seedConnectionString, cancellationToken);
        await MigrateAsync<IdentityDbContext>(IdentityDbContext.ApplyOptions, seedConnectionString, cancellationToken);
        await MigrateAsync<ProjectsDbContext>(ProjectsDbContext.ApplyOptions, seedConnectionString, cancellationToken);
        await MigrateAsync<ArtifactsDbContext>(ArtifactsDbContext.ApplyOptions, seedConnectionString, cancellationToken);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
                EnvironmentName = Environments.Development,
            });
        builder.Host.UseDefaultServiceProvider(static options => { options.ValidateOnBuild = false; options.ValidateScopes = false; });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort()}");
        builder.Logging.ClearProviders();
        builder.Configuration["auth:bootstrap:adminEmail"] = BootstrapEmail;
        builder.Configuration["auth:bootstrap:adminPassword"] = BootstrapPassword;

        var (host, port) = SplitEndpoint(minioEndpoint);
        builder.Configuration["Artifacts:Endpoint"] = $"{host}:{port}";
        builder.Configuration["Artifacts:AccessKey"] = MinioUser;
        builder.Configuration["Artifacts:SecretKey"] = MinioPassword;
        builder.Configuration["Artifacts:Bucket"] = TestBucket;
        builder.Configuration["Artifacts:UseSSL"] = "false";
        builder.Configuration["Artifacts:AutoCreateBucket"] = "true";

        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration)
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);

        application = HostComposer.Compose(builder, HostDatabase.Explicit(connectionString));
        application.MapWorkerRuntime();
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

    [Fact(DisplayName = "Given a leased work item, when the worker uploads a png, then the host returns 204 and the artifact is in MinIO")]
    public async Task WorkerUploadPngReturns204Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, workItemId, workerToken) = await SeedLeasedWorkItemAsync(cancellationToken);

        var payload = new byte[2048];
        Array.Fill(payload, (byte)0xFF);
        using var content = new MultipartFormDataContent("----comuki-test-boundary");
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "preview.png",
        };
        content.Add(fileContent);

        using var worker = new HttpClient { BaseAddress = baseAddress };
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);

        var response = await worker.PostAsync(
            $"/workers/{workItemId}/artifacts",
            content,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var row = await ReadVisualArtifactRowAsync(workItemId, cancellationToken);
        row.ContentType.ShouldBe("image/png");
        row.Filename.ShouldBe("preview.png");
        row.SizeBytes.ShouldBe(payload.Length);
        row.CreatedBy.ShouldBe("worker");
        row.Version.ShouldBe(1);
        row.ProjectId.ShouldBe(projectId.Value);
    }

    [Fact(DisplayName = "Given a leased work item, when the worker uploads a png and a user GETs the content, then the body is served with the png mime type")]
    public async Task AnonymousContentGetIs401AndAuthedGetReturnsPngAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, workItemId, workerToken) = await SeedLeasedWorkItemAsync(cancellationToken);

        var payload = new byte[1024];
        Array.Fill(payload, (byte)0x42);
        var artifactId = await UploadArtifactAsync(workerToken, workItemId, payload, "image/png", "frame.png", cancellationToken);

        // 1. Anonymous GET on the content proxy → 401.
        using (var anon = new HttpClient { BaseAddress = baseAddress })
        {
            var anonymous = await anon.GetAsync(
                $"/api/v1/projects/{projectId.Value}/artifacts/{artifactId}/content",
                cancellationToken);
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // 2. Authenticated GET → 200, Content-Type = image/png, body matches.
        using var admin = await CreateAdminClientAsync(cancellationToken);
        var authed = await admin.GetAsync(
            $"/api/v1/projects/{projectId.Value}/artifacts/{artifactId}/content",
            cancellationToken);

        authed.StatusCode.ShouldBe(HttpStatusCode.OK);
        authed.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        authed.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");

        var body = await authed.Content.ReadAsByteArrayAsync(cancellationToken);
        body.Length.ShouldBe(payload.Length);
        if (!body.SequenceEqual(payload))
        {
            throw new Xunit.Sdk.XunitException(
                $"body length matched ({body.Length}) but content differs; first 32 bytes: "
                + $"{Convert.ToHexString([.. body.Take(32)])} vs expected {Convert.ToHexString([.. payload.Take(32)])}");
        }
    }

    [Fact(DisplayName = "Given an html artifact, when the content proxy serves it, then the strict CSP header is attached")]
    public async Task HtmlContentServedWithStrictCspAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (projectId, workItemId, workerToken) = await SeedLeasedWorkItemAsync(cancellationToken);

        var htmlBody = "<!doctype html><html><body>hello</body></html>"u8.ToArray();
        var artifactId = await UploadArtifactAsync(workerToken, workItemId, htmlBody, "text/html", "card.html", cancellationToken);

        using var admin = await CreateAdminClientAsync(cancellationToken);
        var response = await admin.GetAsync(
            $"/api/v1/projects/{projectId.Value}/artifacts/{artifactId}/content",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        // CSP headers are attached at the response level (HttpResponseHeaders),
        // not the content level — HttpResponseMessage.Headers vs Content.Headers.
        response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'none'");
        csp.ShouldContain("script-src https://cdn.tailwindcss.com");
        csp.ShouldContain("style-src 'unsafe-inline' https://cdn.tailwindcss.com");
    }

    [Fact(DisplayName = "Given a leased work item, when the worker uploads a payload larger than the cap, then the host returns 413")]
    public async Task OversizedPngReturns413Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workItemId, workerToken) = await SeedLeasedWorkItemAsync(cancellationToken);

        var payload = new byte[6 * 1024 * 1024];
        Array.Fill(payload, (byte)0x01);

        using var worker = new HttpClient { BaseAddress = baseAddress };
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);

        using var content = new MultipartFormDataContent("----comuki-test-boundary");
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "huge.png",
        };
        content.Add(fileContent);

        var response = await worker.PostAsync(
            $"/workers/{workItemId}/artifacts",
            content,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact(DisplayName = "Given a worker with a stale lease, when the worker uploads an artifact, then the host returns 409")]
    public async Task NotOwnerReturns409Async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, workItemId, _) = await SeedLeasedWorkItemAsync(cancellationToken);

        // Use a different worker id — the lease on the seeded work item
        // belongs to a different worker, so this upload must 409.
        var intruder = application.Services.GetRequiredService<WorkerTokenIssuer>().Issue(WorkerId.New());

        using var worker = new HttpClient { BaseAddress = baseAddress };
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", intruder);

        using var content = new MultipartFormDataContent("----comuki-test-boundary");
        var fileContent = new ByteArrayContent(new byte[256]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "sneaky.png",
        };
        content.Add(fileContent);

        var response = await worker.PostAsync(
            $"/workers/{workItemId}/artifacts",
            content,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private async Task<Guid> UploadArtifactAsync(
        string workerToken,
        Guid workItemId,
        byte[] payload,
        string contentType,
        string filename,
        CancellationToken cancellationToken)
    {
        using var worker = new HttpClient { BaseAddress = baseAddress };
        worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerToken);

        using var content = new MultipartFormDataContent("----comuki-test-boundary");
        var fileContent = new ByteArrayContent(payload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = filename,
        };
        content.Add(fileContent);

        var response = await worker.PostAsync(
            $"/workers/{workItemId}/artifacts",
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadArtifactIdFromRowAsync(workItemId, cancellationToken);
    }

    private async Task<Guid> ReadArtifactIdFromRowAsync(Guid workItemId, CancellationToken cancellationToken)
    {
        var row = await ReadVisualArtifactRowAsync(workItemId, cancellationToken);
        return row.Id;
    }

    private async Task<Modules.Artifacts.Domain.VisualArtifacts.VisualArtifact> ReadVisualArtifactRowAsync(
        Guid workItemId,
        CancellationToken cancellationToken)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArtifactsDbContext>();
        // The integration test seeds data outside the host pipeline,
        // so the ambient subject-scope is unset; the read sees every
        // row (system consumer).
        var systemScope = scope.ServiceProvider.GetRequiredService<Shared.Kernel.Scoping.ISubjectScopeAccessor>()
            .AsSystem("worker-upload-artifact-test");
        return await db.VisualArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.WorkItemId == workItemId)
            .FirstAsync(cancellationToken);
    }

    private async Task<(ProjectId ProjectId, Guid WorkItemId, string WorkerToken)> SeedLeasedWorkItemAsync(CancellationToken cancellationToken)
    {
        var orchestrationOptions = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(orchestrationOptions, seedConnectionString);
        await using var orchestrationDb = new OrchestrationDbContext(orchestrationOptions.Options);

        var projectId = ProjectId.New();
        var now = DateTimeOffset.UtcNow;
        var workerId = WorkerId.New();

        var run = Run.Create(projectId, now);
        run.TransitionTo(RunStatus.Running, now + TimeSpan.FromMinutes(1));
        orchestrationDb.Runs.Add(run);

        var workItem = WorkItem.Create(
            run.Id,
            "implementer",
            "image:latest",
            "refs/heads/main",
                                 /*lang=json,strict*/
                                 """{"goal":"build a thing"}""",
            WorkItemStatus.Queued,
            now);
        workItem.AssignLease(workerId, now + TimeSpan.FromMinutes(15), now);
        orchestrationDb.WorkItems.Add(workItem);

        await orchestrationDb.SaveChangesAsync(cancellationToken);

        var token = application.Services.GetRequiredService<WorkerTokenIssuer>().Issue(workerId);
        return (projectId, workItem.Id, token);
    }

    private async Task<HttpClient> CreateAdminClientAsync(CancellationToken cancellationToken)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true, CheckCertificateRevocationList = true })
        {
            BaseAddress = baseAddress,
        };
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = BootstrapEmail, password = BootstrapPassword },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return client;
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
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
