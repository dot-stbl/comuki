using Comuki.Modules.Intake.Application.Ports;

namespace Comuki.Modules.Intake.Infrastructure.Persistence.Stores;

/// <summary>
/// <see cref="ISecretResolver"/> over the process environment — the only
/// secret source of the intake module. Connections name env vars; the
/// values never touch the database.
/// </summary>
public sealed class EnvSecretResolver : ISecretResolver
{
    /// <inheritdoc />
    public string? Resolve(string? envName)
    {
        return envName is { Length: > 0 } ? Environment.GetEnvironmentVariable(envName) : null;
    }
}
