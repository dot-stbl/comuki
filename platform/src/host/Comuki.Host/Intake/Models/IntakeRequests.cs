namespace Comuki.Host.Intake.Models;

/// <summary>Native ticket creation body (POST /api/v1/tickets).</summary>
public sealed class CreateNativeTicketRequest
{
    /// <summary>Project the ticket (and its run) belongs to.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Ticket title.</summary>
    public required string Title { get; init; } = string.Empty;

    /// <summary>Ticket body.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Caller-supplied dedupe id; generated when empty.</summary>
    public string? ExternalId { get; init; }

    /// <summary>Author label.</summary>
    public string? Author { get; init; }
}

/// <summary>Inbox claim body (POST /api/v1/inbox/claim).</summary>
public sealed class ClaimTicketRequest
{
    /// <summary>The pending ticket to claim.</summary>
    public required Guid TicketId { get; init; }
}

/// <summary>Source connection creation body (POST /api/v1/sources).</summary>
public sealed class CreateSourceConnectionRequest
{
    /// <summary>Project the connection feeds.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Provider key: github | gitlab | yandex-tracker | jira.</summary>
    public required string Provider { get; init; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; } = string.Empty;

    /// <summary>Provider-specific, non-secret settings (env-var NAMES only).</summary>
    public required string SettingsJson { get; init; } = "{}";

    /// <summary>Env-var name holding the webhook secret.</summary>
    public required string SecretEnvRef { get; init; } = string.Empty;
}

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
