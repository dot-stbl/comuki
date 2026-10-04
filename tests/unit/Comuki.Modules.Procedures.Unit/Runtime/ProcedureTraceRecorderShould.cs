using Comuki.Modules.Procedures.Application.Runtime.Trace;
using Comuki.Modules.Procedures.Domain.Definitions;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Runtime;

/// <summary>
/// Unit tests for task 5.3: the trace recorder builds immutable traces
/// and classifies drift between the planned and observed graphs. A
/// divergence in width (e.g. the plan node wrote 3 lanes instead of
/// the declared 4) is classified within policy when it stays inside
/// the fan-out range.
/// </summary>
public sealed class ProcedureTraceRecorderShould
{
    [Fact(DisplayName = "Given a pinned version, when Record runs, then the trace starts empty")]
    public void TraceStartsEmpty()
    {
        var trace = ProcedureTraceRecorder.Record("v1", "checkout-flow", Guid.NewGuid(), DateTimeOffset.UtcNow);

        trace.PinnedVersionId.ShouldBe("v1");
        trace.ProcedureKey.ShouldBe("checkout-flow");
        trace.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a trace, when Append runs, then a new trace is returned with the event added immutably")]
    public void AppendIsImmutable()
    {
        var trace = ProcedureTraceRecorder.Record("v1", "checkout-flow", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var firstEvent = new TraceEvent("intake", "started", "node started", DateTimeOffset.UtcNow);

        var withEvent = ProcedureTraceRecorder.Append(trace, firstEvent);

        withEvent.Events.ShouldHaveSingleItem();
        trace.Events.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given planned width 3 and observed width 3 with tolerance 1, when drift is classified, then it is within policy")]
    public void SameWidthWithinPolicy()
    {
        var planned = Graph(3);
        var observed = Graph(3);

        var classification = ProcedureTraceRecorder.ClassifyDrift(planned, observed, 1);

        classification.IsWithinPolicy.ShouldBeTrue();
        classification.Summary.ShouldContain("within policy");
    }

    [Fact(DisplayName = "Given planned width 3 and observed width 4 with tolerance 1, when drift is classified, then it is within policy")]
    public void WithinToleranceIsWithinPolicy()
    {
        var planned = Graph(3);
        var observed = Graph(4);

        var classification = ProcedureTraceRecorder.ClassifyDrift(planned, observed, 1);

        classification.IsWithinPolicy.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given planned width 3 and observed width 5 with tolerance 1, when drift is classified, then it is outside policy")]
    public void BeyondToleranceIsOutsidePolicy()
    {
        var planned = Graph(3);
        var observed = Graph(5);

        var classification = ProcedureTraceRecorder.ClassifyDrift(planned, observed, 1);

        classification.IsWithinPolicy.ShouldBeFalse();
        classification.Summary.ShouldContain("outside policy");
    }

    [Fact(DisplayName = "Given an empty planned graph and an empty observed graph, when drift is classified, then it is within policy")]
    public void EmptyGraphsAreWithinPolicy()
    {
        var planned = new ProcedureGraph([], []);
        var observed = new ProcedureGraph([], []);

        var classification = ProcedureTraceRecorder.ClassifyDrift(planned, observed, 0);

        classification.IsWithinPolicy.ShouldBeTrue();
    }

    private static ProcedureGraph Graph(int width)
    {
        var nodes = Enumerable.Range(0, width)
            .Select(static i => new ProcedureNode($"node-{i}", "agent", new Dictionary<string, string>()))
            .ToList();
        return new ProcedureGraph(nodes, []);
    }
}
