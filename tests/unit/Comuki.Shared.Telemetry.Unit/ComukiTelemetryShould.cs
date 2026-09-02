using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Telemetry.Unit;

/// <summary>Process-wide meters/sources and <see cref="ComukiTelemetry.RecordClaim"/>.</summary>
public sealed class ComukiTelemetryShould
{
    [Fact(DisplayName = "Given telemetry statics, when read, then meter and source names match instrumentation")]
    public void ExposeNamedMetersAndSources()
    {
        ComukiTelemetry.QueueMeter.Name.ShouldBe(ComukiInstrumentation.QueueMeterName);
        ComukiTelemetry.RunsMeter.Name.ShouldBe(ComukiInstrumentation.RunsMeterName);
        ComukiTelemetry.ComputeMeter.Name.ShouldBe(ComukiInstrumentation.ComputeMeterName);
        ComukiTelemetry.OrchestrationSource.Name.ShouldBe(ComukiInstrumentation.OrchestrationSourceName);
        ComukiTelemetry.ComputeSource.Name.ShouldBe(ComukiInstrumentation.ComputeSourceName);
        ComukiTelemetry.HostSource.Name.ShouldBe(ComukiInstrumentation.HostSourceName);
    }

    [Fact(DisplayName = "Given a claim hit with an activity, when RecordClaim is called, then the activity gets outcome=hit")]
    public void TagActivityOnHit()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ComukiInstrumentation.OrchestrationSourceName,
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = ComukiTelemetry.OrchestrationSource.StartActivity("claim-test");
        activity.ShouldNotBeNull();

        ComukiTelemetry.RecordClaim(TimeSpan.FromMilliseconds(12), claimedItem: true, activity);

        activity.GetTagItem(ComukiInstrumentation.OutcomeTag).ShouldBe(ComukiInstrumentation.OutcomeHit);
    }

    [Fact(DisplayName = "Given a claim miss without an activity, when RecordClaim is called, then it does not throw")]
    public void TolerateNullActivityOnEmpty()
    {
        ComukiTelemetry.RecordClaim(TimeSpan.FromMilliseconds(1), claimedItem: false, activity: null);
    }
}
