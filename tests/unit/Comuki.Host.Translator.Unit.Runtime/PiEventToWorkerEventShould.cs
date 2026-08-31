using Comuki.Host.Translator.Execution;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Unit tests for the pi-event → WorkerEvent mapping and the run summary
/// accumulation that feeds the StageReport.
/// </summary>
public sealed class PiEventToWorkerEventShould
{
    private const string WorkItemId = "0b6d7c1e-49cc-4a30-9b52-52a04c8e0a70";

    [Fact]
    public void ForwardTextDeltaAsTextActivity()
    {
        var forwarded = PiEventToWorkerEvent.ToForwardEvent(
            WorkItemId,
            new PiEvent.TextDeltaEvent(0, "Hel"));

        forwarded.ShouldNotBeNull();
        forwarded.Activity.ShouldNotBeNull();
        forwarded.Activity.Text.ShouldBe("Hel");
        forwarded.Activity.Tool.ShouldBeNull();
        forwarded.Activity.WorkItemId.ShouldBe(WorkItemId);
    }

    [Fact]
    public void ForwardAuthoritativeAssistantTextAsTextActivity()
    {
        var forwarded = PiEventToWorkerEvent.ToForwardEvent(
            WorkItemId,
            new PiEvent.AssistantTextEvent("Hello world"));

        forwarded.ShouldNotBeNull();
        forwarded.Activity.ShouldNotBeNull();
        forwarded.Activity.Text.ShouldBe("Hello world");
    }

    [Fact]
    public void ForwardToolCallWithRawArgs()
    {
        var forwarded = PiEventToWorkerEvent.ToForwardEvent(
            WorkItemId,
            new PiEvent.ToolCallEvent("Bash", /*lang=json,strict*/ """{"command":"ls"}"""));

        forwarded.ShouldNotBeNull();
        forwarded.Activity.ShouldNotBeNull();
        forwarded.Activity.Tool.ShouldBe("Bash");
        forwarded.Activity.ToolInputJson.ShouldBe(/*lang=json,strict*/ """{"command":"ls"}""");
        forwarded.Activity.Text.ShouldBeNull();
    }

    [Fact]
    public void NotForwardSessionHeaderOrAgentEndOrUnknown()
    {
        PiEventToWorkerEvent.ToForwardEvent(WorkItemId, new PiEvent.SessionHeaderEvent(3, "id", "/work")).ShouldBeNull();
        PiEventToWorkerEvent.ToForwardEvent(WorkItemId, new PiEvent.AgentEndEvent()).ShouldBeNull();
        PiEventToWorkerEvent.ToForwardEvent(WorkItemId, new PiEvent.ResultEvent("success", 1, 0m, "done")).ShouldBeNull();
    }

    [Fact]
    public void AccumulateDeltasThenReplaceWithAuthoritativeText()
    {
        var summary = new WorkerRunSummary();

        summary.Observe(new PiEvent.TextDeltaEvent(0, "Hel"));
        summary.Observe(new PiEvent.TextDeltaEvent(0, "lo"));
        summary.ResultText.ShouldBe("Hello");

        summary.Observe(new PiEvent.AssistantTextEvent("Hello world"));
        summary.ResultText.ShouldBe("Hello world");
    }

    [Fact]
    public void IgnoreNonTextEventsInSummary()
    {
        var summary = new WorkerRunSummary();

        summary.Observe(new PiEvent.SessionHeaderEvent(3, "id", "/work"));
        summary.Observe(new PiEvent.ToolCallEvent("Bash", "{}"));
        summary.Observe(new PiEvent.AgentEndEvent());

        summary.ResultText.ShouldBe(string.Empty);
    }
}
