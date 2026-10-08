using Comuki.Modules.Integrations.Domain.Items;
using Comuki.Modules.Integrations.Domain.Rules;

namespace Comuki.Modules.Integrations.Application.Admission;

/// <summary>
/// The pure admission evaluator: ticket + rules → decision. The first
/// enabled rule (oldest first) whose filter matches the ticket decides
/// the mode; no matching rule means filtered out. No I/O, no state —
/// the truth table lives in the unit tests.
/// </summary>
public static class AdmissionEvaluator
{
    /// <summary>
    /// Evaluates the rules against the ticket.
    /// </summary>
    /// <param name="rules">Enabled rules, oldest first (the caller orders).</param>
    /// <param name="ticket"></param>
    /// <returns>The mode of the first matching rule, or null when filtered out.</returns>
    public static AdmissionMode? Evaluate(IReadOnlyList<AdmissionRule> rules, InboundItem ticket)
    {
        foreach (var rule in rules)
        {
            if (Matches(AdmissionFilter.Parse(rule.FilterJson), ticket))
            {
                return rule.Mode;
            }
        }

        return null;
    }

    /// <summary>Does the ticket pass one filter?</summary>
    /// <param name="filter"></param>
    /// <param name="ticket"></param>
    /// 
    public static bool Matches(AdmissionFilter filter, InboundItem ticket)
    {
        var labelsMatch = filter.LabelsAny.Count == 0
            || ticket.Labels.Any(filter.LabelsAny.Contains);
        var projectsMatch = filter.Projects.Count == 0
            || (ticket.ProjectKey is { } projectKey && filter.Projects.Contains(projectKey));

        return labelsMatch && projectsMatch;
    }
}
