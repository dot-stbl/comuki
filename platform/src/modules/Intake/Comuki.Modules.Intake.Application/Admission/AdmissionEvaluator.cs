using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Application.Admission;

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
    public static AdmissionMode? Evaluate(IReadOnlyList<AdmissionRule> rules, IncomingTicket ticket)
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
    /// <returns></returns>
    public static bool Matches(AdmissionFilter filter, IncomingTicket ticket)
    {
        var labelsMatch = filter.LabelsAny.Count == 0
            || ticket.Labels.Any(filter.LabelsAny.Contains);
        var projectsMatch = filter.Projects.Count == 0
            || (ticket.ProjectKey is { } projectKey && filter.Projects.Contains(projectKey));

        return labelsMatch && projectsMatch;
    }
}
