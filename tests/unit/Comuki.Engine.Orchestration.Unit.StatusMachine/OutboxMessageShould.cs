using Comuki.Engine.Orchestration.Domain.Outbox;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// Invariant guards of <see cref="OutboxMessage"/>: <see cref="OutboxMessage.Create"/>
/// rejects empty type/payload and seeds a row in the pending state;
/// <see cref="OutboxMessage.MarkDispatched"/> transitions the row to
/// dispatched; <see cref="OutboxMessage.RecordFailure"/> increments
/// <see cref="OutboxMessage.Attempts"/> below the cap and
/// dead-letters the row once the budget is exhausted.
/// </summary>
public sealed class OutboxMessageShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given valid args, when Create is called, then the row is pending with attempts zero and timestamps null")]
    public void CreatePendingRow()
    {
        var message = OutboxMessage.Create("orchestration.run.terminated.v1", /*lang=json,strict*/ """{"id":"run-1"}""", now);

        message.Id.ShouldNotBe(Guid.Empty);
        message.Type.ShouldBe("orchestration.run.terminated.v1");
        message.Payload.ShouldBe(/*lang=json,strict*/ """{"id":"run-1"}""");
        message.CreatedAt.ShouldBe(now);
        message.Attempts.ShouldBe(0);
        message.DispatchedAt.ShouldBeNull();
        message.LastError.ShouldBeNull();
        message.DeadLetteredAt.ShouldBeNull();
        message.IsDispatched.ShouldBeFalse();
        message.IsDeadLettered.ShouldBeFalse();
    }

    [Theory(DisplayName = "Given a blank type, when Create is called, then it throws")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectBlankTypeOnCreate(string type)
    {
        Should.Throw<ArgumentException>(() => OutboxMessage.Create(type, /*lang=json,strict*/ """{"id":"x"}""", now));
    }

    [Theory(DisplayName = "Given a blank payload, when Create is called, then it throws")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectBlankPayloadOnCreate(string payload)
    {
        Should.Throw<ArgumentException>(() => OutboxMessage.Create("orchestration.run.terminated.v1", payload, now));
    }

    [Fact(DisplayName = "Given a pending row, when MarkDispatched is called, then dispatched_at is set and is_dispatched is true")]
    public void MarkDispatchedTransitionsToDispatched()
    {
        var message = OutboxMessage.Create("orchestration.run.terminated.v1", /*lang=json,strict*/ """{"id":"run-1"}""", now);
        var dispatchedAt = now.AddSeconds(30);

        message.MarkDispatched(dispatchedAt);

        message.DispatchedAt.ShouldBe(dispatchedAt);
        message.IsDispatched.ShouldBeTrue();
        message.IsDeadLettered.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a pending row below the retry cap, when RecordFailure is called, then attempts increments and last_error is stored")]
    public void RecordFailureBelowCapKeepsRowLive()
    {
        var message = OutboxMessage.Create("orchestration.run.terminated.v1", /*lang=json,strict*/ """{"id":"run-1"}""", now);
        const int maxAttempts = 5;

        message.RecordFailure("upstream timeout", now.AddSeconds(10), maxAttempts);

        message.Attempts.ShouldBe(1);
        message.LastError.ShouldBe("upstream timeout");
        message.DeadLetteredAt.ShouldBeNull();
        message.IsDeadLettered.ShouldBeFalse();
        message.IsDispatched.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a row that reaches the retry cap, when RecordFailure is called, then dead_lettered_at is set and is_dead_lettered is true")]
    public void RecordFailureAtCapDeadLettersRow()
    {
        var message = OutboxMessage.Create("orchestration.run.terminated.v1", /*lang=json,strict*/ """{"id":"run-1"}""", now);
        const int maxAttempts = 2;
        var firstFailureAt = now.AddSeconds(10);
        var secondFailureAt = now.AddSeconds(20);

        message.RecordFailure("upstream timeout", firstFailureAt, maxAttempts);
        message.RecordFailure("upstream still timing out", secondFailureAt, maxAttempts);

        message.Attempts.ShouldBe(2);
        message.LastError.ShouldBe("upstream still timing out");
        message.DeadLetteredAt.ShouldBe(secondFailureAt);
        message.IsDeadLettered.ShouldBeTrue();
        message.IsDispatched.ShouldBeFalse();
    }

    [Theory(DisplayName = "Given a blank error, when RecordFailure is called, then it throws")]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectBlankErrorOnRecordFailure(string error)
    {
        var message = OutboxMessage.Create("orchestration.run.terminated.v1", /*lang=json,strict*/ """{"id":"run-1"}""", now);

        Should.Throw<ArgumentException>(() => message.RecordFailure(error, now, maxAttempts: 5));
    }
}
