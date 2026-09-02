namespace Comuki.Host.Intake.Models;

/// <summary>Admission rule creation body (POST /api/v1/admission-rules).</summary>
public sealed class CreateAdmissionRuleRequest
{
    /// <summary>Project the rule governs.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>watch | inbox.</summary>
    public required string Mode { get; init; } = string.Empty;

    /// <summary>Filter: {"labelsAny": [...], "projects": [...]}.</summary>
    public required string FilterJson { get; init; } = "{}";
}
