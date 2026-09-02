using Comuki.Modules.Intake.Domain.Rules;

namespace Comuki.Modules.Intake.Application.Views;

/// <summary>Read-model of an admission rule.</summary>
/// <param name="Id"></param>
/// <param name="ProjectId"></param>
/// <param name="Mode">watch | inbox.</param>
/// <param name="FilterJson"></param>
/// <param name="Enabled"></param>
public sealed record AdmissionRuleView(
    Guid Id,
    Guid ProjectId,
    string Mode,
    string FilterJson,
    bool Enabled)
{
    /// <summary>Maps the domain entity.</summary>
    /// <param name="rule"></param>
    /// <returns></returns>
    public static AdmissionRuleView Of(AdmissionRule rule)
    {
        return new AdmissionRuleView(
            rule.Id.Value,
            rule.ProjectId.Value,
            rule.Mode.ToString().ToLowerInvariant(),
            rule.FilterJson,
            rule.Enabled);
    }
}
