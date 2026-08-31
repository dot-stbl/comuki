namespace Comuki.Host.Auth;

/// <summary>
/// Runs <see cref="BootstrapAdminSeeder"/> once during host startup —
/// after migrations (the migrator is a separate step) and before the
/// server accepts traffic. A database that is unreachable here fails
/// the boot loudly: identity persistence is essential to this host.
/// </summary>
/// <param name="scopes"></param>
/// <param name="logger"></param>
public sealed class BootstrapAdminStartupService(
    IServiceScopeFactory scopes,
    ILogger<BootstrapAdminStartupService> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        await scope.ServiceProvider.GetRequiredService<BootstrapAdminSeeder>()
            .SeedAsync(cancellationToken);
        logger.LogInformation("Bootstrap admin pass completed");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
