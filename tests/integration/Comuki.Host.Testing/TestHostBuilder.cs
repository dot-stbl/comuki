using Comuki.Engine.Orchestration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Comuki.Host.Testing;

/// <summary>
/// The <see cref="WebApplicationBuilder"/> flags and worker-runtime wiring
/// every integration harness needs before layering its own suite-specific
/// configuration and calling <c>HostComposer.Compose</c>.
/// </summary>
public static class TestHostBuilder
{
    /// <summary>
    /// Creates a builder against <paramref name="connectionString"/> with
    /// every host-wide test flag applied: a random loopback port; DI
    /// validation off (test-only escape hatch — production always
    /// validates at <c>Build()</c>); console logging reset so xUnit's
    /// parallel runner doesn't interleave provider state; and the
    /// orchestration persistence + queue wiring <c>Program</c> does before
    /// <c>Compose</c> (the worker-runtime contract every suite that boots
    /// the real host composition shares — <c>Compose</c> itself resolves
    /// scoped orchestration services the suites and the host's own
    /// endpoints both need).
    /// </summary>
    /// <remarks>
    /// <see cref="Environments.Development"/> is deliberate, not a default
    /// left in place: the production-secret validator (issue #10 T11.4)
    /// short-circuits on non-<c>Production</c>, and switching to
    /// <c>Development</c> would additionally flip on
    /// <c>ValidateScopes</c>, which a handful of module installers
    /// currently trip over (singleton registrations that resolve a scoped
    /// <c>DbContext</c>) — pre-existing, out of scope for a test fixture.
    /// </remarks>
    /// <param name="connectionString">Postgres connection string the harness already migrated with <see cref="HostDatabaseMigrator"/>.</param>
    public static WebApplicationBuilder Create(string connectionString)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HostComposer).Assembly.GetName().Name,
            EnvironmentName = Environments.Development, // test fixture — ProductionSecretValidator short-circuits on non-Production
        });

        builder.Host.UseDefaultServiceProvider(static options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{FreeTcpPort.Next()}");
        builder.Logging.ClearProviders();

        builder.Services
            .AddOrchestrationPersistence(connectionString)
            .AddOrchestrationQueue(builder.Configuration);

        return builder;
    }

    /// <summary>Starts the composed host and resolves the loopback base address Kestrel bound to.</summary>
    /// <param name="application">The <see cref="WebApplication"/> returned by <c>HostComposer.Compose</c>.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    public static async Task<Uri> StartAsync(WebApplication application, CancellationToken cancellationToken)
    {
        await application.StartAsync(cancellationToken);

        return new Uri(
            application.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single());
    }
}
