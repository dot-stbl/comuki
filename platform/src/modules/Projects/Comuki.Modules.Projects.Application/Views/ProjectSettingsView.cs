using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Views;

/// <summary>
/// Read model of per-project settings. <see cref="Version"/> rides along
/// so API clients can echo it into the next PUT (optimistic concurrency).
/// </summary>
public sealed record ProjectSettingsView
{
    public required ProjectId ProjectId { get; init; }

    public required int MinIdle { get; init; }

    public required int MaxConcurrent { get; init; }

    public required int? IdleTtlSeconds { get; init; }

    public required bool ApproveRequired { get; init; }

    public required bool KnowledgeEnabled { get; init; }

    public required bool VerifyEnabled { get; init; }

    public required bool ProxyEnabled { get; init; }

    public required long? SoftBudgetUsdMicros { get; init; }

    public required long? HardBudgetUsdMicros { get; init; }

    /// <summary>Routing mode for user-facing domain types.</summary>
    public required ProjectDomainType DomainType { get; init; }

    /// <summary>Per-project JSON map of <c>domain-type → profile-key</c>.</summary>
    public required string? CustomDomainTypesJson { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int Version { get; init; }
}
