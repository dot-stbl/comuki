namespace Comuki.Host.Brain;

/// <summary>
/// The model emitted an invalid plan and exhausted its retry — the brain
/// call fails rather than shipping a broken decomposition.
/// </summary>
public sealed class BrainInvalidPlanException(IReadOnlyList<string> errors) : Exception(
    "the model's plan stayed invalid after one retry:\n- " + string.Join("\n- ", errors))
{
    /// <summary>The validation errors of the last rejected plan.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;
}
