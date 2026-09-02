using System.Text;
using Comuki.Modules.Intake.Application.Ports;

namespace Comuki.Modules.Intake.Infrastructure.Providers;

/// <summary>
/// The status comment every tracker gets on a terminal run status —
/// one shared text so all providers say the same thing.
/// </summary>
public static class TrackerSyncComments
{
    /// <summary>The comment body for one transition.</summary>
    /// <param name="transition"></param>
    /// <returns></returns>
    public static string Of(TicketTransition transition)
    {
        var builder = new StringBuilder("Comuki run ")
            .Append(transition.RunStatus.ToLowerInvariant())
            .Append(": ")
            .Append(transition.RunUrl);

        if (transition.ExternalUrl is { Length: > 0 } issueUrl)
        {
            builder.Append("\n\nIssue: ").Append(issueUrl);
        }

        return builder.ToString();
    }
}
