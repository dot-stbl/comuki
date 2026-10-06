using Comuki.Modules.Memory.Application.Ranking;
using Comuki.Modules.Memory.Application.Views;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Domain.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// Reciprocal Rank Fusion of the lexical and vector rank lists: k=60,
/// per-list weight 1.0 by default. These tests lock the canonical
/// RRF-k=60 math — if the constant drifts, both the Context Fabric
/// manifest and the spec's "each candidate carries LexicalRank /
/// VectorRank / FusedScore" requirement move with it.
/// </summary>
public sealed class MemoryHybridRankingShould
{
    private static readonly DateTimeOffset now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the same row in both lists, when fused, then its score is the sum of both list contributions")]
    public void FuseSumsContributionsOfOverlappingRows()
    {
        // One row appears in both the lexical top and the vector top.
        // RRF: 1.0 / (60 + 0) + 1.0 / (60 + 0) = 2/61.
        var overlap = Fact("overlap");
        var lexical = new[] { overlap };
        var vector = new[] { overlap };

        var fused = MemoryHybridRanking.Fuse(lexical, vector);

        fused.ShouldHaveSingleItem();
        fused[0].Id.ShouldBe(overlap.Id);
        fused[0].FusedScore.ShouldBe(2f / 61f, 0.0001f);
    }

    [Fact(DisplayName = "Given disjoint lists, when fused, then each list's contribution stands alone")]
    public void FuseKeepsDisjointRowsOnTheirOwnRanker()
    {
        // Two rows, only in the lexical list (no vector matches).
        // RRF: 1/61 + 1/62, ordered desc.
        var first = Fact("lexical-1");
        var second = Fact("lexical-2");
        var lexical = new[] { first, second };
        var vector = Array.Empty<MemoryFactView>();

        var fused = MemoryHybridRanking.Fuse(lexical, vector);

        fused.Count.ShouldBe(2);
        fused[0].Id.ShouldBe(first.Id);
        fused[1].Id.ShouldBe(second.Id);
        fused[0].FusedScore.ShouldBe(1f / 61f, 0.0001f);
        fused[1].FusedScore.ShouldBe(1f / 62f, 0.0001f);
    }

    [Fact(DisplayName = "Given two empty lists, when fused, then the result is empty")]
    public void FuseEmptyListsReturnsEmpty()
    {
        MemoryHybridRanking.Fuse([], []).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a limit, when fused, then only the top-N by FusedScore survive")]
    public void FuseAppliesLimitAfterOrdering()
    {
        var rows = Enumerable.Range(0, 10).Select(static i => Fact($"row-{i}")).ToArray();
        // All in lexical list, none in vector — fused score is
        // 1 / (60 + rank). Limit 3 keeps the top three by FusedScore.
        var fused = MemoryHybridRanking.Fuse(rows, [], limit: 3);

        fused.Count.ShouldBe(3);
        fused[0].Id.ShouldBe(rows[0].Id);
        fused[1].Id.ShouldBe(rows[1].Id);
        fused[2].Id.ShouldBe(rows[2].Id);
    }

    [Fact(DisplayName = "Given the manifest constants, when read, then k is 60 and default weights are 1.0 each")]
    public void ManifestConstantsHoldCanonicalValues()
    {
        // The Context Fabric manifest will record these — keeping them
        // in one place prevents drift between the RRF math and the
        // manifest.
        MemoryHybridRanking.K.ShouldBe(60);
        MemoryHybridRanking.DefaultLexicalWeight.ShouldBe(1.0f);
        MemoryHybridRanking.DefaultVectorWeight.ShouldBe(1.0f);
    }

    [Fact(DisplayName = "Given a non-default per-list weight, when fused, then the contribution scales with it")]
    public void FuseRespectsPerListWeights()
    {
        var row = Fact("weighted");
        var fused = MemoryHybridRanking.Fuse(
            [row],
            [row],
            lexicalWeight: 2.0f,
            vectorWeight: 1.0f);

        fused.ShouldHaveSingleItem();
        // 2.0 / 61 + 1.0 / 61 = 3/61.
        fused[0].FusedScore.ShouldBe(3f / 61f, 0.0001f);
    }

    private static MemoryFactView Fact(string topicKey)
    {
        return new MemoryFactView(
            Id: MemoryFactId.New(),
            Scope: MemoryScope.User,
            SubjectId: "user-1",
            Kind: MemoryFactKind.Standing,
            TopicKey: topicKey,
            Text: $"text about {topicKey}",
            Source: MemorySource.Chat,
            CreatedBy: "user-1",
            CreatedAt: now);
    }
}
