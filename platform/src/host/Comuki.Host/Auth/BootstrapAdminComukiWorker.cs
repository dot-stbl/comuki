using Comuki.Shared.Bootstrap.Workers;

namespace Comuki.Host.Auth;

/// <summary>
/// Runs <see cref="BootstrapAdminSeeder"/> once during host startup, behind
/// the comuki worker registry — after migrations (the migrator is a separate
/// step). A database that is unreachable here no longer crashes the boot:
/// the failed cycle is logged as an error and the worker shows unhealthy on
/// <c>/api/v1/workers/background</c> (identity persistence is still
/// essential to this host — the signal moved from a dead process to a red
/// status an operator sees).
/// </summary>
public sealed class BootstrapAdminComukiWorker : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "bootstrap-admin";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Startup();

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        await context.Services.GetRequiredService<BootstrapAdminSeeder>()
            .SeedAsync(cancellationToken);
        context.Logger.LogInformation("Bootstrap admin pass completed");

        return WorkerResult.Ok("admin pass completed");
    }
}
