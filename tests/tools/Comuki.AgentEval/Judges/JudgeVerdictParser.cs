using System.Text.Json;
using Comuki.AgentEval.Corpus;

namespace Comuki.AgentEval.Judges;

/// <summary>
/// Strict, side-effect-free parser for the LLM-as-judge verdict payload.
/// Validates every field the rubric declares, recomputes the
/// weighted <c>OverallScore</c> from the per-criterion scores, and
/// surfaces the model's self-reported <c>overallScore</c> only as a
/// debug field — the authoritative score on <see cref="JudgeVerdict"/>
/// is always the scorer-recomputed value.
/// </summary>
public static class JudgeVerdictParser
{
    /// <summary>
    /// Parses <paramref name="rawJson"/> against <paramref name="rubric"/>'s
    /// declared criteria, returning a parsed <see cref="JudgeVerdict"/> with
    /// the authoritative recomputed <c>OverallScore</c>. Never throws —
    /// any <c>JsonException</c> or validation failure returns
    /// <c>false</c> with a non-null <paramref name="error"/>.
    /// </summary>
    public static bool TryParse(string rawJson, EvalRubric rubric, out JudgeVerdict? verdict, out string? error)
    {
        verdict = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = "raw JSON is empty";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawJson);
        }
        catch (JsonException exception)
        {
            error = $"malformed JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "verdict root must be a JSON object";
                return false;
            }

            if (!root.TryGetProperty("scores", out var scoresElement)
                || scoresElement.ValueKind != JsonValueKind.Array)
            {
                error = "verdict.scores must be an array";
                return false;
            }

            var observedScores = new List<JudgeCriterionScore>(scoresElement.GetArrayLength());
            var seenCriterionIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var scoreElement in scoresElement.EnumerateArray())
            {
                if (scoreElement.ValueKind != JsonValueKind.Object)
                {
                    error = "verdict.scores[] entries must be JSON objects";
                    return false;
                }

                if (!scoreElement.TryGetProperty("criterionId", out var criterionIdElement)
                    || criterionIdElement.ValueKind != JsonValueKind.String
                    || criterionIdElement.GetString() is not { } criterionId)
                {
                    error = "verdict.scores[].criterionId must be a string";
                    return false;
                }

                if (!scoreElement.TryGetProperty("score", out var scoreValueElement)
                    || !scoreValueElement.TryGetDouble(out var scoreValue))
                {
                    error = $"verdict.scores[criterionId={criterionId}].score must be a number";
                    return false;
                }

                if (scoreValue is < 0 or > 1)
                {
                    error = $"verdict.scores[criterionId={criterionId}].score ({scoreValue}) must be in [0, 1]";
                    return false;
                }

                if (!seenCriterionIds.Add(criterionId))
                {
                    error = $"verdict.scores[] contains a duplicate criterionId '{criterionId}'";
                    return false;
                }

                var rationale = scoreElement.TryGetProperty("rationale", out var rationaleElement)
                                && rationaleElement.ValueKind == JsonValueKind.String
                    ? rationaleElement.GetString() ?? string.Empty
                    : string.Empty;

                observedScores.Add(new JudgeCriterionScore(criterionId, scoreValue, rationale));
            }

            var declared = rubric.Criteria;
            var declaredIds = new HashSet<string>(declared.Select(static criterion => criterion.Id), StringComparer.Ordinal);

            foreach (var observedCriterionId in seenCriterionIds)
            {
                if (!declaredIds.Contains(observedCriterionId))
                {
                    error = $"verdict.scores[] contains criterionId '{observedCriterionId}' which is not declared in the rubric";
                    return false;
                }
            }

            foreach (var declaredCriterion in declared)
            {
                if (!seenCriterionIds.Contains(declaredCriterion.Id))
                {
                    error = $"verdict.scores[] is missing the declared criterionId '{declaredCriterion.Id}'";
                    return false;
                }
            }

            var verdictValue = root.TryGetProperty("verdict", out var verdictElement)
                               && verdictElement.ValueKind == JsonValueKind.String
                ? verdictElement.GetString() ?? string.Empty
                : string.Empty;

            var notes = root.TryGetProperty("notes", out var notesElement)
                        && notesElement.ValueKind == JsonValueKind.String
                ? notesElement.GetString() ?? string.Empty
                : string.Empty;

            // Recompute the weighted average ourselves — never trust the model's
            // own overallScore field. Weight normalization is first done by
            // dividing each criterion's weight by the sum.
            var weightSum = declared.Sum(static criterion => criterion.Weight);
            if (weightSum <= 0)
            {
                error = "rubric weights sum to zero or negative; cannot compute weighted average";
                return false;
            }

            var weightedSum = 0d;
            foreach (var observedScore in observedScores)
            {
                var declaredCriterion = declared.First(criterion => criterion.Id == observedScore.CriterionId);
                weightedSum += observedScore.Score * (declaredCriterion.Weight / weightSum);
            }

            verdict = new JudgeVerdict(observedScores, weightedSum, verdictValue, notes);
            return true;
        }
    }
}
