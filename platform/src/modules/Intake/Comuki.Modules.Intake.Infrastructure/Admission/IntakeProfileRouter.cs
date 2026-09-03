using System.Text.Json;
using Comuki.Modules.Intake.Application.Ports.Admission;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;

namespace Comuki.Modules.Intake.Infrastructure.Admission;

/// <summary>
/// Default profile router — reads a per-connection <c>profileKey</c>
/// override from the settings jsonb, falls back to the ticket kind
/// (PR-kind → <c>pr-review</c>, issue → <paramref name="issueDefaultProfileKey"/>).
/// Reads are tolerant: a missing field, a broken json or a non-string
/// value silently uses the fallback — never throws.
/// </summary>
/// <param name="issueDefaultProfileKey">
/// Profile key used for issue-kind tickets when no per-connection
/// override is set; typically bound from <c>Intake:Worker:IssueDefaultProfileKey</c>.
/// </param>
public sealed class IntakeProfileRouter(string issueDefaultProfileKey) : IIntakeProfileRouter
{
    /// <inheritdoc />
    public string ResolveProfileKey(SourceConnection? connection, IncomingTicket ticket)
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

    private string DefaultFor(IncomingTicket ticket)
    {
        return ticket.Kind switch
        {
            InboundTicketKind.PullRequest => PrReviewProfileKey,
            _ => issueDefaultProfileKey,
        };
    }

    /// <summary>The inbound PR-review profile key (matches <c>control-plane/profiles/pr-review.md</c>).</summary>
    public const string PrReviewProfileKey = "pr-review";
}
