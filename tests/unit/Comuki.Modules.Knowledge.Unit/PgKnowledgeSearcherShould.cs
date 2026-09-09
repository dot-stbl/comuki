using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="PgKnowledgeSearcher"/>: validation gates
/// (blank query, out-of-range topK / minSimilarity) throw before any DB
/// is touched. The DB-bound paths (probe → embed → cosine SELECT → row
/// mapping) live in the integration suite under Testcontainers.PostgreSql
/// — the production class uses raw SQL (Npgsql connection cast,
/// pgvector <c>embedding &lt;=&gt;</c> operator) that EF's in-memory
/// provider cannot simulate.
/// </summary>
public sealed class PgKnowledgeSearcherShould
{
    [Fact(DisplayName = "Given a blank query, when SearchAsync is called, then it throws InvalidOperationException before any DB is touched")]
    public async Task BlankQueryThrowsAsync()
    {
        var contextFactory = Substitute.For<IDbContextFactory<KnowledgeDbContext>>();
        var embedder = new NoopEmbeddingClient(EmbeddingSql.Dimensions);
        var searcher = NewSearcher(contextFactory, embedder);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await searcher.SearchAsync(
                query: "   ",
                projectId: null,
                topK: 5,
                minSimilarity: 0.5f,
                cancellationToken: TestContext.Current.CancellationToken));

        await contextFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(TestContext.Current.CancellationToken);
    }

    [Theory(DisplayName = "Given topK outside [1, 1000], when SearchAsync is called, then ArgumentOutOfRangeException is thrown")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task TopKOutOfRangeThrowsAsync(int topK)
    {
        var contextFactory = Substitute.For<IDbContextFactory<KnowledgeDbContext>>();
        var embedder = new NoopEmbeddingClient(EmbeddingSql.Dimensions);
        var searcher = NewSearcher(contextFactory, embedder);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await searcher.SearchAsync(
                query: "ok",
                projectId: null,
                topK: topK,
                minSimilarity: 0.5f,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory(DisplayName = "Given minSimilarity outside [0.0, 1.0], when SearchAsync is called, then ArgumentOutOfRangeException is thrown")]
    [InlineData(-0.01f)]
    [InlineData(-1f)]
    [InlineData(1.01f)]
    [InlineData(2f)]
    public async Task MinSimilarityOutOfRangeThrowsAsync(float minSimilarity)
    {
        var contextFactory = Substitute.For<IDbContextFactory<KnowledgeDbContext>>();
        var embedder = new NoopEmbeddingClient(EmbeddingSql.Dimensions);
        var searcher = NewSearcher(contextFactory, embedder);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await searcher.SearchAsync(
                query: "ok",
                projectId: null,
                topK: 5,
                minSimilarity: minSimilarity,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private static PgKnowledgeSearcher NewSearcher(
        IDbContextFactory<KnowledgeDbContext> contextFactory,
        IEmbeddingClient embedder)
    {
        return new PgKnowledgeSearcher(
            contextFactory,
            embedder,
            NullLogger<PgKnowledgeSearcher>.Instance);
    }
}
