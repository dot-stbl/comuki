using Comuki.Host.Testing.Fixtures;
using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Integration;

/// <summary>
/// <see cref="PgKnowledgeSearcher"/> against a real, migrated
/// <c>pgvector/pgvector:pg16</c> Postgres (the WS2 shared fixture) —
/// exactly the DB-bound territory the unit suite
/// (<c>Comuki.Modules.Knowledge.Unit.PgKnowledgeSearcherShould</c>)
/// explicitly disclaims: the pgvector availability probe, the
/// deterministic-embedder round trip through the real cosine
/// <c>embedding &lt;=&gt;</c> SELECT, row mapping, the
/// <c>minSimilarity</c> threshold, and the SQL-side scope-widening clause
/// (<c>@unrestricted OR project_id IS NULL OR project_id = ANY(...)</c>)
/// for a caller that never names a project at all — the unit suite can
/// only prove the pre-DB refusal when a caller names a foreign project
/// explicitly. Every row is written through <see cref="PgKnowledgeIngestor"/>
/// so seeding exercises the same write path (insert + raw-SQL embedding
/// UPDATE) a real ingest call takes, rather than poking rows in directly.
/// </summary>
/// <remarks>
/// The embedder is <see cref="NoopEmbeddingClient"/> — a deterministic,
/// non-semantic hash-of-text vector (same text always yields the same
/// vector; two different texts yield unrelated vectors with no
/// guaranteed near-zero similarity, unlike a real embedding model — two
/// arbitrary literals were observed at 0.28 cosine similarity during
/// authoring, not near zero). Every assertion below is built from that
/// guarantee alone: a query that repeats a chunk's own exact text always
/// self-matches at a similarity safely above 0.99, and two independently
/// hand-picked distinct literal strings are, for all practical purposes,
/// never within 0.99 of each other. Nothing here assumes unrelated texts
/// land "near zero" — a <c>minSimilarity: 0f</c> search can legitimately
/// surface an unrelated-but-visible row, so scope-visibility assertions
/// below check "is this specific chunk present" (membership), never "is
/// the result set empty."
/// </remarks>
/// <param name="postgres">The collection's shared Postgres (<see cref="KnowledgeIntegrationCollection"/>) — reset to empty for every test, migrated once for the whole run.</param>
[Collection(nameof(KnowledgeIntegrationCollection))]
public sealed class PgKnowledgeSearcherShould(PostgresCollectionFixture postgres) : IAsyncLifetime
{
    private readonly AsyncLocalSubjectScopeAccessor scopeAccessor = new();
    private readonly NoopEmbeddingClient embedder = new(EmbeddingSql.Dimensions);

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider services = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private PgKnowledgeIngestor ingestor = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private PgKnowledgeSearcher searcher = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await postgres.ResetDatabaseAsync();

        var collection = new ServiceCollection();
        collection.AddDbContextFactory<KnowledgeDbContext>(
            options => KnowledgeDbContext.ApplyOptions(options, postgres.ConnectionString));
        services = collection.BuildServiceProvider();

        var contextFactory = services.GetRequiredService<IDbContextFactory<KnowledgeDbContext>>();
        var ingestOptions = Options.Create(new KnowledgeIngestOptions());

        ingestor = new PgKnowledgeIngestor(
            contextFactory,
            embedder,
            ingestOptions,
            scopeAccessor,
            TimeProvider.System,
            NullLogger<PgKnowledgeIngestor>.Instance);
        searcher = new PgKnowledgeSearcher(
            contextFactory,
            embedder,
            scopeAccessor,
            NullLogger<PgKnowledgeSearcher>.Instance);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return services.DisposeAsync();
    }

    [Fact(DisplayName = "Given a chunk ingested with its embedding backfilled, when SearchAsync repeats the chunk's own text, then the real pgvector cosine SELECT returns it as a near-exact top hit")]
    public async Task ReturnTheIngestedChunkAsTopHitAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string text = "Alpha project onboarding runbook: rotate the deploy key every quarter.";

        SourceDocumentId documentId;
        using (scopeAccessor.AsSystem("test-seeder"))
        {
            var result = await IngestAsync(projectId: null, text, cancellationToken);
            documentId = result.SourceDocumentId;
        }

        IReadOnlyList<KnowledgeSearchHit> hits;
        using (scopeAccessor.AsSystem("test-reader"))
        {
            hits = await searcher.SearchAsync(text, projectId: null, topK: 5, minSimilarity: 0f, cancellationToken: cancellationToken);
        }

        hits.ShouldHaveSingleItem();
        hits[0].SourceDocumentId.ShouldBe(documentId);
        hits[0].ChunkText.ShouldBe(text);
        hits[0].Similarity.ShouldBeGreaterThan(0.99f);
    }

    [Fact(DisplayName = "Given chunks in project A, project B and the global corpus, when a caller scoped only to project A searches each chunk's own text, then project A's and the global chunk return but project B's does not")]
    public async Task ExcludeAnotherProjectsChunkFromScopedSearchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var projectA = ProjectId.New();
        var projectB = ProjectId.New();
        const string textA = "Project A: renew the TLS certificate before it expires.";
        const string textB = "Project B: archive the retired worker image.";
        const string textGlobal = "Global corpus: house style guide for commit messages.";

        using (scopeAccessor.AsSystem("test-seeder"))
        {
            await IngestAsync(projectA.Value, textA, cancellationToken);
            await IngestAsync(projectB.Value, textB, cancellationToken);
            await IngestAsync(projectId: null, textGlobal, cancellationToken);
        }

        var scopedToA = new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [projectA]);

        async Task<IReadOnlyList<KnowledgeSearchHit>> SearchAsAScopedCallerAsync(string query)
        {
            using (scopeAccessor.Begin(scopedToA))
            {
                return await searcher.SearchAsync(query, projectId: null, topK: 5, minSimilarity: 0f, cancellationToken: cancellationToken);
            }
        }

        // Own-project and global chunks are reachable through a self-match
        // query (near-1.0 similarity, so a low minSimilarity never hides
        // them). Project B's chunk must never surface for this caller —
        // asserted on membership, not on an empty result set: at
        // minSimilarity=0f every visible row can legitimately surface
        // regardless of how relevant it is to the query (the fake embedder
        // gives no guarantee an unrelated text's cross-similarity is near
        // zero), so "the result set is empty" is not a safe assertion —
        // "project B's chunk specifically is never in it" is.
        var hitsForA = await SearchAsAScopedCallerAsync(textA);
        var hitsForGlobal = await SearchAsAScopedCallerAsync(textGlobal);
        var hitsForB = await SearchAsAScopedCallerAsync(textB);

        hitsForA.ShouldContain(hit => hit.ChunkText == textA);
        hitsForGlobal.ShouldContain(hit => hit.ChunkText == textGlobal);

        hitsForA.ShouldNotContain(hit => hit.ChunkText == textB);
        hitsForGlobal.ShouldNotContain(hit => hit.ChunkText == textB);
        hitsForB.ShouldNotContain(hit => hit.ChunkText == textB);
    }

    [Fact(DisplayName = "Given a chunk and a minSimilarity threshold, when SearchAsync is called with an on-topic vs. an unrelated query, then only the query that clears the threshold returns a hit")]
    public async Task FilterOutHitsBelowMinSimilarityAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string chunkText = "Espresso brewing pressure profile for a V60 pour.";
        const string unrelatedQuery = "Quarterly board deck slide numbering convention.";
        const float highThreshold = 0.99f;

        using (scopeAccessor.AsSystem("test-seeder"))
        {
            await IngestAsync(projectId: null, chunkText, cancellationToken);
        }

        using (scopeAccessor.AsSystem("test-reader"))
        {
            var onTopicHits = await searcher.SearchAsync(chunkText, projectId: null, topK: 5, minSimilarity: highThreshold, cancellationToken: cancellationToken);
            var unrelatedHits = await searcher.SearchAsync(unrelatedQuery, projectId: null, topK: 5, minSimilarity: highThreshold, cancellationToken: cancellationToken);

            onTopicHits.ShouldHaveSingleItem().Similarity.ShouldBeGreaterThanOrEqualTo(highThreshold);
            unrelatedHits.ShouldBeEmpty();
        }
    }

    private Task<KnowledgeIngestResult> IngestAsync(Guid? projectId, string text, CancellationToken cancellationToken)
    {
        return ingestor.IngestAsync(
            projectId: projectId,
            title: "Doc",
            source: SourceKindKeys.Upload,
            sourceRef: $"{Guid.NewGuid():N}.md",
            mimeType: "text/markdown",
            text: text,
            cancellationToken: cancellationToken);
    }
}
