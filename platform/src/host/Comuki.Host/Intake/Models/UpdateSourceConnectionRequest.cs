namespace Comuki.Host.Intake.Models;

/// <summary>Source connection partial update body (PUT /api/v1/sources/{id}).</summary>
public sealed class UpdateSourceConnectionRequest
{
    /// <summary>New name; null = keep.</summary>
    public string? Name { get; init; }

    /// <summary>New settings json; null = keep.</summary>
    public string? SettingsJson { get; init; }

    /// <summary>New secret env name; null = keep.</summary>
    public string? SecretEnvRef { get; init; }

    /// <summary>Enable/disable; null = keep.</summary>
    public bool? Enabled { get; init; }
}
