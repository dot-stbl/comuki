using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Domain.Rules;

/// <summary>
/// The per-project admission rule: which mode tickets of the project run
/// in, and the filter deciding which tickets are admitted. The filter
/// jsonb shape is <c>{"labelsAny": ["comuki", "bug"], "projects":
/// ["dot-stbl/comuki"]}</c> — both lists optional, empty means "any".
/// </summary>
public sealed class AdmissionRule
{
    internal AdmissionRule()
    {
    }

    /// <summary>Strong-typed rule id (UUIDv7).</summary>
    public AdmissionRuleId Id { get; private set; }

    /// <summary>Project the rule governs.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Watch (auto-run) or Inbox (manual claim).</summary>
    public AdmissionMode Mode { get; private set; }

    /// <summary>Filter jsonb consumed by the admission evaluator.</summary>
    public string FilterJson { get; private set; } = string.Empty;

    /// <summary>Disabled rules are ignored by the admission flow.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When the rule was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an enabled rule.</summary>
    /// <param name="projectId"></param>
    /// <param name="mode"></param>
    /// <param name="filterJson"></param>
    /// <param name="now"></param>
    public static AdmissionRule Create(ProjectId projectId, AdmissionMode mode, string filterJson, DateTimeOffset now)
    {
        return new AdmissionRule
        {
            Id = AdmissionRuleId.New(),
            ProjectId = projectId,
            Mode = mode,
            FilterJson = filterJson,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Partial update: a null field leaves the stored value untouched.</summary>
    /// <param name="mode"></param>
    /// <param name="filterJson"></param>
    /// <param name="enabled"></param>
    /// <param name="now"></param>
    public void Update(AdmissionMode? mode, string? filterJson, bool? enabled, DateTimeOffset now)
    {
        if (mode is { } nextMode)
        {
            Mode = nextMode;
        }

        if (filterJson is { } nextFilter)
        {
            FilterJson = nextFilter;
        }

        if (enabled is { } nextEnabled)
        {
            Enabled = nextEnabled;
        }

        UpdatedAt = now;
    }
}
