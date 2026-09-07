using System.Text.Json;
using Comuki.Shared.Contracts.Realtime;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Contracts.Unit.Realtime;

/// <summary>
/// Sanity coverage for the realtime DTOs after the move from
/// <c>Comuki.Host.Realtime.Models</c> to <c>Comuki.Shared.Contracts.Realtime</c>.
/// The dashboard SignalR client still observes the same JSON wire names
/// (PascalCase, record positional properties) — these tests pin the
/// contract so future refactors of the records do not silently drift
/// the broadcast shape.
/// </summary>
public sealed class RealtimeContractRecordShould
{
    [Fact(DisplayName = "Given a RunEventView, when serialised to JSON, then the wire name of OccurredAtUnixMs is preserved as a PascalCase property")]
    public void RunEventViewJsonPropertyNames()
    {
        var view = new RunEventView(
            RunId: Guid.Empty,
            Type: "x",
            WorkItemId: null,
            OccurredAtUnixMs: 0,
            PayloadJson: null,
            PayloadOmitted: false);

        var json = JsonSerializer.Serialize(view);

        json.ShouldContain("\"RunId\":");
        json.ShouldContain("\"Type\":");
        json.ShouldContain("\"WorkItemId\":");
        json.ShouldContain("\"OccurredAtUnixMs\":");
        json.ShouldContain("\"PayloadJson\":");
        json.ShouldContain("\"PayloadOmitted\":");
    }

    [Fact(DisplayName = "Given an AttentionView, when serialised to JSON, then the wire name of AttentionKind is preserved as a PascalCase property")]
    public void AttentionViewJsonPropertyNames()
    {
        var view = new AttentionView(
            RunId: Guid.Empty,
            ProjectId: Guid.Empty,
            WorkItemId: null,
            Status: "Failed",
            AttentionKind: "failed",
            OccurredAtUnixMs: 0);

        var json = JsonSerializer.Serialize(view);

        json.ShouldContain("\"RunId\":");
        json.ShouldContain("\"ProjectId\":");
        json.ShouldContain("\"WorkItemId\":");
        json.ShouldContain("\"Status\":");
        json.ShouldContain("\"AttentionKind\":");
        json.ShouldContain("\"OccurredAtUnixMs\":");
    }

    [Fact(DisplayName = "Given the realtime transport methods, when read, then the wire constants match the C# hub callbacks the host broadcasts")]
    public void RealtimeTransportMethodsMatchHostConstants()
    {
        // The broadcaster in Comuki.Host reads these values verbatim and
        // passes them to IHubContext.Clients.Group.SendAsync(method, payload).
        // Renaming the constants on either side desyncs the dashboard from
        // the host — this test pins the contract.
        RealtimeTransportMethods.RunEvent.ShouldBe("RunEvent");
        RealtimeTransportMethods.Attention.ShouldBe("Attention");
    }
}
