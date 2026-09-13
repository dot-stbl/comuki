using Comuki.Modules.Knowledge.Application;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="PgKnowledgeSearcher"/>: validation gates
/// (blank query, out-of-range topK / minSimilarity) and the scope guard
/// (a caller naming a project outside its own scope) throw/refuse before
/// any DB is touched. The DB-bound paths (probe → embed → cosine SELECT
/// → row mapping, and the SQL-side scope widening for a caller that
/// names no project at all) live in the integration suite under
/// Testcontainers.PostgreSql — the production class uses raw SQL
/// (Npgsql connection cast, pgvector <c>embedding &lt;=&gt;</c> operator,
/// <c>= ANY(uuid[])</c>) that EF's in-memory provider cannot simulate,
/// and no such Knowledge integration project exists yet in this repo (see
/// <c>PgKnowledgeIngestorShould.DbBoundPathsAreCoveredByIntegrationTests</c>
/// for the same caveat on the write side).
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

    [Fact(DisplayName = "Given a caller scoped to one project, when SearchAsync is called for a different project, then it returns empty without touching the DB (leak 1 — read)")]
    public async Task RefuseSearchForProjectOutsideScopeAsync()
    {
        // Before the fix this method took no scope at all: any caller —
        // scoped to project A or not — got every project's chunks back
        // whenever it (or the endpoint on its behalf) omitted projectId,
        // and a caller that DID name a foreign project got that project's
        // chunks too, since the SQL predicate never checked who was
        // asking. This proves a project-A-only caller now gets nothing
        // for project B, and never even opens a context to ask.
        var contextFactory = Substitute.For<IDbContextFactory<KnowledgeDbContext>>();
        var embedder = new NoopEmbeddingClient(EmbeddingSql.Dimensions);
        var ownProject = ProjectId.New();
        var foreignProject = Guid.NewGuid();
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        scopeAccessor.Current.Returns(new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [ownProject]));
        var searcher = NewSearcher(contextFactory, embedder, scopeAccessor);

        var hits = await searcher.SearchAsync(
            query: "anything",
            projectId: foreignProject,
            topK: 5,
            minSimilarity: 0.5f,
            cancellationToken: TestContext.Current.CancellationToken);

        hits.ShouldBeEmpty();
        await contextFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "Given an unrestricted caller, when SearchAsync is called for any project, then the scope guard does not refuse it")]
    public async Task AllowUnrestrictedCallerAnyProjectAsync()
    {
        // An unrestricted (platform-scope / system) caller must still see
        // everything — only a restricted caller is narrowed. This does
        // not assert on rows (that needs a real Postgres connection); it
        // asserts the guard lets the call proceed to open a context.
        var contextFactory = Substitute.For<IDbContextFactory<KnowledgeDbContext>>();
        contextFactory
            .CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no real database in this unit test"));
        var embedder = new NoopEmbeddingClient(EmbeddingSql.Dimensions);
        var scopeAccessor = Substitute.For<ISubjectScopeAccessor>();
        scopeAccessor.Current.Returns(SubjectScope.ForSystem("test"));
        var searcher = NewSearcher(contextFactory, embedder, scopeAccessor);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await searcher.SearchAsync(
                query: "anything",
                projectId: Guid.NewGuid(),
                topK: 5,
                minSimilarity: 0.5f,
                cancellationToken: TestContext.Current.CancellationToken));

        await contextFactory.Received(1).CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    private static PgKnowledgeSearcher NewSearcher(
        IDbContextFactory<KnowledgeDbContext> contextFactory,
        IEmbeddingClient embedder,
        ISubjectScopeAccessor? scopeAccessor = null)
    {
        var scope = scopeAccessor ?? UnrestrictedAccessor();
        return new PgKnowledgeSearcher(
            contextFactory,
            embedder,
            scope,
            NullLogger<PgKnowledgeSearcher>.Instance);
    }

    private static ISubjectScopeAccessor UnrestrictedAccessor()
    {
        var accessor = Substitute.For<ISubjectScopeAccessor>();
        accessor.Current.Returns(SubjectScope.ForSystem("test"));
        return accessor;
    }
}
