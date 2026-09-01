using System.Collections.Frozen;
using FluentValidation;

namespace Comuki.Shared.Contracts.Plans;

/// <summary>
/// Structural validation of a <see cref="Plan"/>: at least one item, unique
/// keys, non-empty profile/brief, dependency references that exist, no
/// self-reference and no cycles. Used by both the chat graph (brain output
/// gate before the approve card) and any future plan producer.
/// </summary>
public sealed class PlanValidator : AbstractValidator<Plan>
{
    /// <summary>Maximum items a plan may carry (guard against brain slop).</summary>
    public const int MaxItems = 64;

    /// <summary>Maximum brief length per item.</summary>
    public const int MaxBriefLength = 8000;

    /// <summary>Initializes the rules.</summary>
    public PlanValidator()
    {
        RuleFor(static plan => plan.Items)
            .NotEmpty()
            .WithMessage("plan must contain at least one item")
            .Must(static items => items.Count <= MaxItems)
            .WithMessage($"plan must not exceed {MaxItems} items");

        RuleForEach(static plan => plan.Items)
            .Must(static item => !string.IsNullOrWhiteSpace(item.Key))
            .WithMessage("every plan item needs a non-empty key")
            .Must(static item => item.Key.Length <= 64)
            .WithMessage("plan item keys must be 64 characters or fewer");

        RuleForEach(static plan => plan.Items)
            .Must(static item => !string.IsNullOrWhiteSpace(item.ProfileKey))
            .WithMessage("every plan item needs a non-empty profileKey");

        RuleForEach(static plan => plan.Items)
            .Must(static item => !string.IsNullOrWhiteSpace(item.Brief))
            .WithMessage("every plan item needs a non-empty brief")
            .Must(static item => item.Brief.Length <= MaxBriefLength)
            .WithMessage($"plan item briefs must be {MaxBriefLength} characters or fewer");

        RuleFor(static plan => plan.Items)
            .Must(PlanValidatorRules.HaveUniqueKeys)
            .WithMessage("plan item keys must be unique");

        RuleFor(static plan => plan.Items)
            .Must(PlanValidatorRules.HaveResolvableDependencies)
            .WithMessage("plan dependsOn references a key that does not exist");

        RuleFor(static plan => plan.Items)
            .Must(static items => items.All(static item => !item.DependsOn.Contains(item.Key, StringComparer.Ordinal)))
            .WithMessage("a plan item cannot depend on itself");

        RuleFor(static plan => plan.Items)
            .Must(PlanValidatorRules.BeAcyclic)
            .WithMessage("plan dependencies must not contain cycles");
    }
}

/// <summary>Key-set and graph checks backing <see cref="PlanValidator"/> rules.</summary>
file static class PlanValidatorRules
{
    public static bool HaveUniqueKeys(IReadOnlyList<PlanItem> items)
    {
        var keys = items.Select(static item => item.Key).ToFrozenSet(StringComparer.Ordinal);
        return keys.Count == items.Count;
    }

    public static bool HaveResolvableDependencies(IReadOnlyList<PlanItem> items)
    {
        var keys = items.Select(static item => item.Key).ToFrozenSet(StringComparer.Ordinal);
        return items.All(item => item.DependsOn.All(keys.Contains));
    }

    public static bool BeAcyclic(IReadOnlyList<PlanItem> items)
    {
        // duplicate keys and dangling references are reported by the
        // dedicated rules; acyclicity must not throw on those shapes
        var byKey = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!byKey.TryAdd(item.Key, item.DependsOn))
            {
                return false;
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var done = new HashSet<string>(StringComparer.Ordinal);

        return items.All(item => ReachesNoCycle(item.Key, byKey, visiting, done));

        static bool ReachesNoCycle(
            string key,
            Dictionary<string, IReadOnlyList<string>> byKey,
            HashSet<string> visiting,
            HashSet<string> done)
        {
            if (done.Contains(key))
            {
                return true;
            }

            if (!visiting.Add(key))
            {
                return false;
            }

            if (!byKey[key].All(dependency => byKey.ContainsKey(dependency) && ReachesNoCycle(dependency, byKey, visiting, done)))
            {
                return false;
            }

            visiting.Remove(key);
            done.Add(key);
            return true;
        }
    }
}
