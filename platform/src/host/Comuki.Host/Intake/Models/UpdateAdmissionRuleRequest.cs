namespace Comuki.Host.Intake.Models;

/// <summary>Admission rule partial update body (PUT /api/v1/admission-rules/{id}).</summary>
public sealed class UpdateAdmissionRuleRequest
{
    /// <summary>watch | inbox; null = keep.</summary>
    public string? Mode { get; init; }

    /// <summary>New filter json; null = keep.</summary>
    public string? FilterJson { get; init; }

    /// <summary>Enable/disable; null = keep.</summary>
    public bool? Enabled { get; init; }
}
