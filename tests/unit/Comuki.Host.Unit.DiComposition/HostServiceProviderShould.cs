using Comuki.Engine.Orchestration.Application;
using Comuki.Host.Testing;
using Comuki.Host.Workers;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Comuki.Host.Unit.DiComposition;

/// <summary>
/// Builds the EXACT service graph <c>Program.cs</c> composes (orchestration
/// persistence/queue/application + worker runtime + <see cref="HostComposer"/>)
/// with <c>ValidateOnBuild</c>/<c>ValidateScopes</c> forced on, regardless of
/// environment — <see cref="TestHostBuilder.Create"/> disables both for its
/// own (DB-needing) integration suites, so this test overrides that back on.
/// No live Postgres/MinIO needed: DI validation is a static call-site graph
/// check, it never opens a connection. Regression coverage for issue #156
/// (captive-dependency / missing-registration class of boot failures).
/// </summary>
public sealed class HostServiceProviderShould
{
    [Fact(DisplayName = "Given the full host composition, when built with ValidateOnBuild+ValidateScopes, then it composes without a captive-dependency or missing-registration error")]
    public async Task ComposeCleanlyAsync()
    {
        var connectionString = "Host=127.0.0.1;Port=1;Database=di-validation;Username=probe;Password=probe";
        var builder = TestHostBuilder.Create(connectionString);
        builder.Services
            .AddOrchestrationApplication()
            .AddWorkerRuntime(builder.Configuration);
        builder.Host.UseDefaultServiceProvider(static options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        // Minimal extra config HostComposer.ComposeAsync's module installers
        // read directly off IConfiguration before Build() (not through
        // IOptions ValidateOnStart, which never runs at Build() time) —
        // keep this list to what's actually needed; trim anything you find
        // isn't.
        TestBootstrapAdmin.Configure(builder.Configuration);

        // Skip the boot migrator: this test only proves DI composition
        // (ValidateOnBuild/ValidateScopes), and the migrator would
        // otherwise try to open the probe Postgres at 127.0.0.1:1 and
        // fail with a socket error. Production never sets this flag.
        builder.Configuration["Host:Testing:SkipBootMigrations"] = "true";

        var app = await HostComposer.ComposeAsync(builder, HostDatabase.Explicit(connectionString));
        await app.DisposeAsync();
    }
}
