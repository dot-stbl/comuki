using Comuki.Modules.Intake.Domain.Deliveries;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Sync;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>SyncJob retry/backoff and IntakeDelivery outcome recording.</summary>
public sealed class SyncJobAndDeliveryShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 23, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a fresh SyncJob, when MarkDone is called, then status is Done")]
    public void MarkSyncJobDone()
    {
        var job = SyncJob.Create(
            IncomingTicketId.New(),
            SourceConnectionId.New(),
            RunId.New(),
            "COM-1",
            "https://example.com/1",
            "Succeeded",
            now);

        job.MarkDone(now.AddMinutes(1));

        job.Status.ShouldBe(SyncJobStatus.Done);
        job.Attempts.ShouldBe(0);
    }

    [Fact(DisplayName = "Given a SyncJob under budget, when MarkFailed is called, then backoff schedules next attempt")]
    public void RetryWithBackoff()
    {
        var job = SyncJob.Create(
            IncomingTicketId.New(),
            SourceConnectionId.New(),
            RunId.New(),
            "COM-1",
            "https://example.com/1",
            "Failed",
            now);
        var backoff = TimeSpan.FromSeconds(2);

        job.MarkFailed("timeout", maxAttempts: 3, backoff, now);

        job.Status.ShouldBe(SyncJobStatus.Pending);
        job.Attempts.ShouldBe(1);
        job.LastError.ShouldBe("timeout");
        job.NextAttemptAt.ShouldBe(now + backoff);
    }

    [Fact(DisplayName = "Given a SyncJob at budget, when MarkFailed is called, then status is Failed")]
    public void ExhaustAttempts()
    {
        var job = SyncJob.Create(
            IncomingTicketId.New(),
            SourceConnectionId.New(),
            RunId.New(),
            "COM-1",
            "https://example.com/1",
            "Failed",
            now);

        job.MarkFailed("e1", 2, TimeSpan.FromSeconds(1), now);
        job.MarkFailed("e2", 2, TimeSpan.FromSeconds(1), now.AddSeconds(5));

        job.Status.ShouldBe(SyncJobStatus.Failed);
        job.Attempts.ShouldBe(2);
        job.LastError.ShouldBe("e2");
    }

    [Fact(DisplayName = "Given an IntakeDelivery, when SetOutcome is called, then outcome and detail are stored")]
    public void RecordDeliveryOutcome()
    {
        var delivery = IntakeDelivery.Create("github", "delivery-1", now);

        delivery.SetOutcome(DeliveryOutcomes.Admitted, "COM-1");

        delivery.Source.ShouldBe("github");
        delivery.DeliveryId.ShouldBe("delivery-1");
        delivery.Outcome.ShouldBe(DeliveryOutcomes.Admitted);
        delivery.Detail.ShouldBe("COM-1");
        delivery.Id.Version.ShouldBe(7);
    }
}
