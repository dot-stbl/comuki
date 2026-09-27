using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// WS9 (issue #87): the admission idempotency hook on <see cref="Run"/>.
/// <see cref="Run.Create(ProjectId, DateTimeOffset, string?)"/>'s optional
/// 3rd argument is the inbox <c>message_id</c> the launcher claimed before
/// creating the run, so a losing admission caller can find the winner's
/// run. It defaults to null and is never mutated — these tests pin both
/// shapes so a future regression on the overload signature fails loudly
/// rather than silently admitting duplicates.
/// </summary>
public sealed class RunAdmissionMessageIdShould
{
    [Fact(DisplayName = "Given no admission message id, when Run.Create is called, then AdmissionMessageId is null")]
    public void DefaultsToNull()
    {
        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow);

        run.AdmissionMessageId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an admission message id, when Run.Create is called, then AdmissionMessageId is set")]
    public void HonoursProvidedValue()
    {
        var messageId = "admission-" + Guid.NewGuid().ToString("D");

        var run = Run.Create(ProjectId.New(), DateTimeOffset.UtcNow, messageId);

        run.AdmissionMessageId.ShouldBe(messageId);
    }
}
