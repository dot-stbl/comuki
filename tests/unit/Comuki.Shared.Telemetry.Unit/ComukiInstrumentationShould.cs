using Shouldly;
using Xunit;

namespace Comuki.Shared.Telemetry.Unit;

/// <summary>Stable naming contract of meters, sources and instruments.</summary>
public sealed class ComukiInstrumentationShould
{
    [Fact(DisplayName = "Given instrumentation constants, when read, then names stay lowercase comuki.* / assembly sources")]
    public void ExposeStableNames()
    {
        ComukiInstrumentation.QueueMeterName.ShouldBe("comuki.queue");
        ComukiInstrumentation.RunsMeterName.ShouldBe("comuki.runs");
        ComukiInstrumentation.ComputeMeterName.ShouldBe("comuki.compute");
        ComukiInstrumentation.OrchestrationSourceName.ShouldBe("Comuki.Engine.Orchestration");
        ComukiInstrumentation.ComputeSourceName.ShouldBe("Comuki.Engine.Compute");
        ComukiInstrumentation.HostSourceName.ShouldBe("Comuki.Host");
        ComukiInstrumentation.ClaimDurationName.ShouldBe("comuki.queue.claim.duration");
        ComukiInstrumentation.OutcomeHit.ShouldBe("hit");
        ComukiInstrumentation.OutcomeEmpty.ShouldBe("empty");
    }
}
