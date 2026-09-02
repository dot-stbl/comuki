namespace Comuki.Modules.Intake.Application.Ports;

/// <summary>
/// Resolves an environment-variable name to its value — the only way
/// the intake code touches secrets. Secrets never live in the database;
/// connections store env-var names (<c>SourceConnection.SecretEnvRef</c>
/// and the API-token env name inside the settings json).
/// </summary>
public interface ISecretResolver
{
    /// <summary>Resolves the env var; null when the name is empty or the variable is unset.</summary>
    /// <param name="envName"></param>
    /// <returns></returns>
    public string? Resolve(string? envName);
}
