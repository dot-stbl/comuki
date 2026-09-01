namespace Comuki.Shared.Contracts.Plans;

/// <summary>Outcome of validating a plan; valid exactly when <see cref="Errors"/> is empty.</summary>
/// <param name="Errors">Human-readable validation errors, empty when the plan is valid.</param>
public sealed record PlanValidationResult(IReadOnlyList<string> Errors)
{
    /// <summary>True when no errors were found.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>The single no-error result.</summary>
    public static readonly PlanValidationResult Valid = new([]);
}
