namespace Comuki.AgentTest.Runner.Execution;

/// <summary>A ticket seeded through the real webhook — the run/work-item the real queue claim will match against.</summary>
/// <param name="RunId">The created run's id.</param>
/// <param name="WorkItemId">The created work item's id.</param>
public sealed record SeededWorkItem(Guid RunId, Guid WorkItemId);
