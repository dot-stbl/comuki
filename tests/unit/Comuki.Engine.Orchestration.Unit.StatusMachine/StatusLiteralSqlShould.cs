using Comuki.Engine.Orchestration.Infrastructure.EscalationTimeout;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Configurations;
using Comuki.Engine.Orchestration.Infrastructure.Persistence.Stores;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Pins the raw-SQL and partial-index predicates composed from
/// <c>nameof(WorkItemStatus.*)</c>, <c>nameof(RunStatus.*)</c> and
/// <c>nameof(MergeQueueStatus.*)</c> to their exact, historically-emitted
/// text. These constants are <c>internal</c> (visible here via
/// <c>InternalsVisibleTo</c>) — the composition is pure string building, no
/// I/O, so a unit test is the right level; the hygiene fix that introduced
/// <c>nameof</c> here promises the literal text never moved. A future
/// enum-member rename now fails the build (the <c>nameof</c> no longer
/// compiles) instead of silently producing a predicate that stops matching
/// rows in Postgres.
/// </summary>
public sealed class StatusLiteralSqlShould
{
    [Fact(DisplayName = "Given the claim SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeClaimSql()
    {
        WorkItemQueueSql.ClaimSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET status = 'Running', leased_by = @workerId, lease_until = @leaseUntil, "
            + "    heartbeat_at = @now, attempt = attempt + 1, updated_at = @now "
            + "WHERE id IN ( "
            + "    SELECT id FROM orchestration.work_items "
            + "    WHERE status = 'Queued' "
            + "      AND profile_key = @profileKey "
            + "      AND image = @image "
            + "      AND profiles_ref = @profilesRef "
            + "    ORDER BY created_at "
            + "    LIMIT 1 "
            + "    FOR UPDATE SKIP LOCKED "
            + ") "
            + "RETURNING id, run_id, profile_key, brief, lease_until, attempt");
    }

    [Fact(DisplayName = "Given the heartbeat SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeHeartbeatSql()
    {
        WorkItemQueueSql.HeartbeatSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET lease_until = @leaseUntil, heartbeat_at = @now, updated_at = @now "
            + "WHERE id = @workItemId AND leased_by = @workerId "
            + "  AND status = 'Running' AND lease_until > @now");
    }

    [Fact(DisplayName = "Given the complete SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeCompleteSql()
    {
        WorkItemQueueSql.CompleteSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET status = 'Succeeded', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
            + "WHERE id = @workItemId AND leased_by = @workerId AND status = 'Running' "
            + "RETURNING run_id");
    }

    [Fact(DisplayName = "Given the fail SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeFailSql()
    {
        WorkItemQueueSql.FailSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET status = 'Failed', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
            + "WHERE id = @workItemId AND leased_by = @workerId AND status = 'Running' "
            + "RETURNING run_id");
    }

    [Fact(DisplayName = "Given the reap-requeue SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeReapRequeueSql()
    {
        WorkItemQueueSql.ReapRequeueSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET status = 'Queued', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
            + "WHERE status = 'Running' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt < @maxAttempts "
            + "RETURNING id, run_id, attempt");
    }

    [Fact(DisplayName = "Given the reap-fail SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeReapFailSql()
    {
        WorkItemQueueSql.ReapFailSql.ShouldBe(
            "UPDATE orchestration.work_items "
            + "SET status = 'Failed', leased_by = NULL, lease_until = NULL, heartbeat_at = NULL, updated_at = @now "
            + "WHERE status = 'Running' AND lease_until IS NOT NULL AND lease_until <= @cutoff AND attempt >= @maxAttempts "
            + "RETURNING id, run_id, attempt");
    }

    [Fact(DisplayName = "Given the merge-queue claim SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeMergeQueueClaimNextSql()
    {
        MergeQueueStoreSql.ClaimNextSql.ShouldBe(
            "UPDATE orchestration.merge_queue "
            + "SET status = 'InProgress', claimed_by = @operatorId, claimed_at = @now "
            + "WHERE id IN ( "
            + "    SELECT id FROM orchestration.merge_queue "
            + "    WHERE status = 'Pending' "
            + "      AND (@projectId IS NULL OR project_id = @projectId) "
            + "    ORDER BY enqueued_at "
            + "    LIMIT 1 "
            + "    FOR UPDATE SKIP LOCKED "
            + ") "
            + "RETURNING id, project_id, branch_name, pull_request_url, status, conflict_resolution, "
            + "          enqueued_at, claimed_by, claimed_at, merged_at, abandoned_at, abandoned_reason, notes");
    }

    [Fact(DisplayName = "Given the escalation archive SQL, when composed, then it matches the historical text byte-for-byte")]
    public void ComposeEscalationArchiveSql()
    {
        EscalationTimeoutSql.ArchiveSql.ShouldBe(
            "UPDATE orchestration.runs "
            + "SET status = 'Cancelled', updated_at = @now "
            + "WHERE status = 'Escalated' AND updated_at < @cutoff "
            + "RETURNING id, updated_at");
    }

    [Fact(DisplayName = "Given the work-items active-status partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposeWorkItemActiveStatusesFilter()
    {
        WorkItemConfiguration.ActiveStatusesFilter.ShouldBe("status IN ('Queued', 'Running')");
    }

    [Fact(DisplayName = "Given the work-items claim-only partial index, when composed, then the predicate matches the historical text byte-for-byte")]
    public void ComposeWorkItemQueuedStatusFilter()
    {
        WorkItemConfiguration.QueuedStatusFilter.ShouldBe("status = 'Queued'");
    }
}
