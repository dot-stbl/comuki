using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Comuki.Shared.Contracts.Plans;

/// <summary>
/// Parse/serialize bridge between the model-produced plan JSON and the
/// <see cref="Plan"/> contract. Malformed JSON and validation errors are
/// reported together — the brain feeds them back to the model on its one
/// retry. Uses the frozen <see cref="JsonSerializerOptions.Web"/> (no
/// hand-rolled options).
/// </summary>
public static class PlanJson
{
    /// <summary>
    /// Parses and validates a plan document in one pass; on success the
    /// deserialized plan is handed back for caller-side follow-up checks
    /// (e.g. profile-key existence against a catalog).
    /// </summary>
    /// <param name="json">The plan JSON produced by the model.</param>
    /// <param name="plan">The parsed plan; non-null exactly when the return is true.</param>
    /// <param name="validation">Shape and rule errors; valid exactly when the return is true.</param>
    /// <returns>True when the JSON parsed and passed validation.</returns>
    public static bool TryParse(
        string json,
        [NotNullWhen(true)] out Plan? plan,
        out PlanValidationResult validation)
    {
        try
        {
            plan = JsonSerializer.Deserialize<Plan>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            plan = null;
            validation = new PlanValidationResult([$"plan json is malformed: {exception.Message}"]);
            return false;
        }

        validation = PlanValidator.Validate(plan);
        if (!validation.IsValid || plan is null)
        {
            plan = null;
            return false;
        }

        return true;
    }

    /// <summary>Parses and validates a plan document; errors cover both JSON shape and plan rules.</summary>
    /// <param name="json"></param>
    public static PlanValidationResult Parse(string json)
    {
        TryParse(json, out _, out var validation);
        return validation;
    }

    /// <summary>Serializes a plan in the canonical camelCase wire form (what finalJson carries).</summary>
    /// <param name="plan"></param>
    public static string Serialize(Plan plan)
    {
        return JsonSerializer.Serialize(plan, JsonSerializerOptions.Web);
    }
}
