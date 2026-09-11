using Comuki.Modules.Intake.Infrastructure.Persistence.Configurations;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Pins the partial-index predicates composed from
/// <c>nameof(IntakeTicketStatus.*)</c> / <c>nameof(SyncJobStatus.*)</c> to
/// their exact, historically-emitted text. These constants are
/// <c>internal</c> (visible here via <c>InternalsVisibleTo</c>) — pure
/// string composition, no I/O — so a rename of a status member now fails
/// the build instead of silently leaving one of these filters stale.
/// </summary>
public sealed class IntakeStatusFilterSqlShould
{
    [Fact(DisplayName = "Given the active-tickets partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposeActiveStatusesFilter()
    {
        IncomingTicketConfiguration.ActiveStatusesFilter.ShouldBe("status IN ('Pending', 'Claimed')");
    }

    [Fact(DisplayName = "Given the pending-tickets partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposePendingStatusFilter()
    {
        IncomingTicketConfiguration.PendingStatusFilter.ShouldBe("status = 'Pending'");
    }

    [Fact(DisplayName = "Given the claimed-tickets partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposeClaimedStatusFilter()
    {
        IncomingTicketConfiguration.ClaimedStatusFilter.ShouldBe("status = 'Claimed'");
    }

    [Fact(DisplayName = "Given the due-sync-jobs partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposeSyncJobPendingStatusFilter()
    {
        SyncJobConfiguration.PendingStatusFilter.ShouldBe("status = 'Pending'");
    }
}
