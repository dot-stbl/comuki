namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>The inbound ticket a scenario seeds through the real webhook endpoint.</summary>
public sealed record ScenarioTicket
{
    /// <summary>Intake source key (matches an intake webhook fixture shape) — <c>github</c> is the only wired provider today.</summary>
    public string Source { get; init; } = "github";

    /// <summary>Ticket title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Ticket body (markdown).</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Labels the admission rule filters on.</summary>
    public List<string> Labels { get; init; } = [];

    /// <summary>The fixture target repo the worker's workspace is prepared from.</summary>
    public ScenarioTargetRepo TargetRepo { get; init; } = new();
}
