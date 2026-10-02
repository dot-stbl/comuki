using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Shouldly;
using Xunit;
namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for task 3.1: the GraphPatch drafting service stamps the
/// durable proposal object with a fresh id, the supplied identity, and
/// the clock's UTC timestamp. The service does not call the validator
/// or the diff computer — the brain's draft is a value the chat
/// surface keeps; the diff path is opt-in.
/// </summary>
public sealed class GraphPatchDraftingServiceShould
{
    [Fact(DisplayName = "Given a draft request, when the patch is drafted, then the id is fresh, the identity is preserved, and the timestamp is the clock's UTC now")]
    public async Task DraftStampsDurableProposalAsync()
    {
        var frozen = new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(frozen);
        var service = new GraphPatchDraftingService(clock);

        var request = new DraftGraphPatchRequest(
            BaseVersionId: "v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "checkout-flow",
            Operations: [new GraphPatchOperation.AddNode(new Domain.Definitions.Elements.ProcedureNode("verify", "verify", new Dictionary<string, string>()))],
            Rationale: "skip review for docs-only",
            DraftedBy: new GraphPatchDraftedBy("brain-session-42", GraphPatchDraftedKind.Brain));

        var patch = await service.DraftAsync(request, TestContext.Current.CancellationToken);

        patch.Id.ShouldNotBe(default);
        patch.BaseVersionId.ShouldBe("v1");
        patch.ProcedureKey.ShouldBe("checkout-flow");
        patch.DraftedBy.Identity.ShouldBe("brain-session-42");
        patch.DraftedBy.Kind.ShouldBe(GraphPatchDraftedKind.Brain);
        patch.DraftedAt.ShouldBe(frozen);
        patch.Rationale.ShouldBe("skip review for docs-only");
        patch.Operations.ShouldHaveSingleItem();
    }

    [Fact(DisplayName = "Given a draft request with null rationale, when the patch is drafted, then the rationale is stored as the empty string (never null)")]
    public async Task NullRationaleBecomesEmptyStringAsync()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var service = new GraphPatchDraftingService(clock);

        var request = new DraftGraphPatchRequest(
            BaseVersionId: "v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "checkout-flow",
            Operations: [],
            Rationale: null!,
            DraftedBy: new GraphPatchDraftedBy("tester", GraphPatchDraftedKind.Operator));

        var patch = await service.DraftAsync(request, TestContext.Current.CancellationToken);

        patch.Rationale.ShouldBe(string.Empty);
    }

    [Fact(DisplayName = "Given two draft requests, when drafted in sequence, then each patch has a distinct id (no collision on the same identity)")]
    public async Task SuccessiveDraftsHaveDistinctIdsAsync()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var service = new GraphPatchDraftingService(clock);
        var request = new DraftGraphPatchRequest(
            BaseVersionId: "v1",
            ProjectId: Guid.NewGuid(),
            ProcedureKey: "checkout-flow",
            Operations: [],
            Rationale: "x",
            DraftedBy: new GraphPatchDraftedBy("tester", GraphPatchDraftedKind.Operator));

        var first = await service.DraftAsync(request, TestContext.Current.CancellationToken);
        var second = await service.DraftAsync(request, TestContext.Current.CancellationToken);

        first.Id.ShouldNotBe(second.Id);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
