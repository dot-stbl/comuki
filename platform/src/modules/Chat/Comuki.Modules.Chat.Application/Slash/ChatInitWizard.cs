using System.Text.Json;

namespace Comuki.Modules.Chat.Application.Slash;

/// <summary>
/// The /init onboarding wizard — skeleton edition (issue #5 slice B): each
/// step is a static prompt, every user message becomes the answer of the
/// current step, and after the last step the wizard emits a summary JSON.
/// Real wiring (creating the project, compute settings, model defaults,
/// knowledge seed) is a documented follow-up — nothing is persisted beyond
/// the session transcript here.
/// </summary>
public static class ChatInitWizard
{
    /// <summary>Wizard step: the prompt shown and the answer key it collects.</summary>
    /// <param name="Key">Stable answer key inside the summary JSON.</param>
    /// <param name="Prompt">Question asked for this step.</param>
    public sealed record Step(string Key, string Prompt);

    /// <summary>The wizard steps in order.</summary>
    public static readonly IReadOnlyList<Step> Steps =
    [
        new Step("repo", "Step 1/4 — which git repository should this workspace build against? Paste the URL."),
        new Step("compute", "Step 2/4 — which compute backend should run workers? (docker | k3s)"),
        new Step("models", "Step 3/4 — which model provider should the brain and workers use? (provider name or endpoint)"),
        new Step("knowledge", "Step 4/4 — seed knowledge: paste a URL or summary to store first, or type 'skip'."),
    ];

    /// <summary>Serializes the collected answers with their keys.</summary>
    /// <param name="answers">Answer key → value collected so far.</param>
    /// <returns>Compact JSON object string.</returns>
    public static string ToAnswersJson(IReadOnlyDictionary<string, string> answers)
    {
        return JsonSerializer.Serialize(answers, JsonSerializerOptions.Web);
    }

    /// <summary>Reads back the answers JSON written by <see cref="ToAnswersJson"/>.</summary>
    /// <param name="json">Answers JSON (empty string allowed → empty map).</param>
    /// <returns>Answer map.</returns>
    public static IReadOnlyDictionary<string, string> FromAnswersJson(string json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>Builds the final summary the wizard emits instead of wiring anything.</summary>
    /// <param name="answers">Collected answers.</param>
    /// <returns>Summary JSON with the wizard marker and every answer.</returns>
    public static string ToSummaryJson(IReadOnlyDictionary<string, string> answers)
    {
        return JsonSerializer.Serialize(new ChatInitSummary { Answers = answers }, JsonSerializerOptions.Web);
    }
}

/// <summary>Summary payload of a finished wizard — the documented hand-off shape for the wiring follow-up.</summary>
internal sealed class ChatInitSummary
{
    /// <summary>Constant <c>init</c> marker of the wizard that produced the summary.</summary>
    public string Wizard { get; init; } = "init";

    /// <summary>Collected step answers by key.</summary>
    public IReadOnlyDictionary<string, string> Answers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
