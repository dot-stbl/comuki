using System.Text.Json;

namespace Comuki.Modules.Intake.Application.Admission;

/// <summary>
/// The parsed admission filter (the rule's filter jsonb): a ticket is
/// admitted when its labels intersect <see cref="LabelsAny"/> (any label
/// matches; an empty set matches everything) AND its
/// <c>ProjectKey</c> is in <see cref="Projects"/> (empty = any project).
/// Parsing is tolerant: unknown fields are ignored, a null or malformed
/// json degrades to the match-everything filter — never throws.
/// </summary>
/// <param name="LabelsAny">Label matchers (case-insensitive).</param>
/// <param name="Projects">Tracker-side project/repo/queue keys (case-insensitive).</param>
public sealed record AdmissionFilter(IReadOnlySet<string> LabelsAny, IReadOnlySet<string> Projects)
{
    /// <summary>Match-everything filter.</summary>
    public static AdmissionFilter Any { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Tolerant parse of the rule's filter jsonb.</summary>
    /// <param name="filterJson"></param>
    /// <returns></returns>
    public static AdmissionFilter Parse(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
        {
            return Any;
        }

        try
        {
            using var document = JsonDocument.Parse(filterJson, documentOptions);
            var root = document.RootElement;

            var labels = ReadStringSet(root, "labelsAny");
            var projects = ReadStringSet(root, "projects");

            return new AdmissionFilter(labels, projects);
        }
        catch (JsonException)
        {
            return Any;
        }
    }

    private static readonly JsonDocumentOptions documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static IReadOnlySet<string> ReadStringSet(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind is not JsonValueKind.Array)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }
}
