using System.Text.Json;
using Comuki.Modules.Integrations.Application.Ports.Admission;
using Comuki.Modules.Integrations.Domain.Connections;
using Comuki.Modules.Integrations.Domain.Items;

namespace Comuki.Modules.Integrations.Infrastructure.Admission;

/// <summary>
/// Default profile router — reads a per-connection <c>profileKey</c>
/// override from the settings jsonb, falls back to the ticket kind
/// (PR-kind → <c>pr-review</c>, issue → <paramref name="issueDefaultProfileKey"/>).
/// Reads are tolerant: a missing field, a broken json or a non-string
/// value silently uses the fallback — never throws.
/// </summary>
/// <param name="issueDefaultProfileKey">
/// Profile key used for issue-kind tickets when no per-connection
/// override is set; typically bound from <c>Integrations:Worker:IssueDefaultProfileKey</c>.
/// </param>
public sealed class IntegrationProfileRouter(string issueDefaultProfileKey) : IIntegrationProfileRouter
{
    /// <inheritdoc />
    public string ResolveProfileKey(SourceConnection? connection, InboundItem ticket)
    {
        return connection is { } && ReadOverride(connection.SettingsJson) is { } overrideKey
            ? overrideKey
            : DefaultFor(ticket);
    }

    private static string? ReadOverride(string settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (!document.RootElement.TryGetProperty("profileKey", out var element)
                || element.ValueKind is not JsonValueKind.String)
            {
                return null;
            }

            var value = element.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string DefaultFor(InboundItem ticket)
    {
        return ticket.Kind switch
        {
            InboundItemKind.PullRequest => PrReviewProfileKey,
            _ => issueDefaultProfileKey,
        };
    }

    /// <summary>The inbound PR-review profile key (matches <c>control-plane/profiles/pr-review.md</c>).</summary>
    public const string PrReviewProfileKey = "pr-review";
}
