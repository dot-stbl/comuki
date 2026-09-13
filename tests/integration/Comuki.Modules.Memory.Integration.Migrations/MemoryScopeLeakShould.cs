using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// Leak 2 against a REAL pgvector database: <see cref="EfMemoryStore"/>'s
/// cosine-ranked path (<c>MemoryFactSql.CosineSearchSql</c>) runs raw SQL
/// outside EF's model, so <see cref="MemoryDbContext"/>'s
/// <c>HasQueryFilter</c> on <see cref="MemoryFact"/> — proven against EF
/// Core InMemory in <c>Comuki.Modules.Memory.Unit</c> — cannot reach it.
/// Before the fix, a restricted caller supplying the exact scope, subject
/// and a matching query embedding got the other project's/subject's fact
/// back through this path exactly as freely as through the (now-filtered)
/// LINQ fallback. Registers a REAL <see cref="AsyncLocalSubjectScopeAccessor"/>
/// so <see cref="MemoryDbContext"/> is wired the same way the production
/// Brain host now wires it.
/// </summary>
public sealed class MemoryScopeLeakShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider provider = null!;

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ISubjectScopeAccessor scopeAccessor = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISubjectScopeAccessor, AsyncLocalSubjectScopeAccessor>();
        services.AddMemoryPersistence(container.GetConnectionString());
        services.AddSingleton(TimeProvider.System);
        provider = services.BuildServiceProvider();
        scopeAccessor = provider.GetRequiredService<ISubjectScopeAccessor>();

        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given a project-scoped fact embedded for cosine search, when a caller restricted to a different project searches with the exact matching embedding, then nothing comes back")]
    public async Task RestrictedScopeCannotReachAnotherProjectsFactByCosineSearchAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var otherProject = ProjectId.New();
        var embedding = Vector(0, 0.1f);

        // Seeded as a system consumer — the write path is not the leak
        // under test and must not be gated by the reader's own scope.
        using (scopeAccessor.AsSystem("seed"))
        {
            await store.WriteAsync(
                new MemoryFactWrite(
                    MemoryScope.Project,
                    otherProject.Value.ToString(),
                    MemoryFactKind.Standing,
                    "roadmap",
                    "other project's secret plan",
                    MemorySource.Chat,
                    "tester",
                    embedding),
                cancellationToken);
        }

        using (scopeAccessor.Begin(new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [ProjectId.New()])))
        {
            var results = await store.SearchAsync(
                new MemoryFactQuery(
                    Scope: MemoryScope.Project,
                    SubjectId: otherProject.Value.ToString(),
                    Embedding: embedding),
                cancellationToken);

            results.ShouldBeEmpty();
        }
    }

    [Fact(DisplayName = "Given the same project-scoped fact, when the owning project's caller searches with a matching embedding, then it is returned")]
    public async Task OwningProjectScopeReachesItsOwnFactByCosineSearchAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownProject = ProjectId.New();
        var embedding = Vector(0, 0.1f);

        using (scopeAccessor.AsSystem("seed"))
        {
            await store.WriteAsync(
                new MemoryFactWrite(
                    MemoryScope.Project,
                    ownProject.Value.ToString(),
                    MemoryFactKind.Standing,
                    "roadmap",
                    "own project's plan",
                    MemorySource.Chat,
                    "tester",
                    embedding),
                cancellationToken);
        }

        using (scopeAccessor.Begin(new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [ownProject])))
        {
            var results = await store.SearchAsync(
                new MemoryFactQuery(
                    Scope: MemoryScope.Project,
                    SubjectId: ownProject.Value.ToString(),
                    Embedding: embedding),
                cancellationToken);

            results.ShouldHaveSingleItem().Text.ShouldBe("own project's plan");
        }
    }

    [Fact(DisplayName = "Given a global fact embedded for cosine search, when a restricted caller with no project assignments searches, then the global fact still comes back")]
    public async Task RestrictedScopeStillReachesGlobalFactsByCosineSearchAsync()
    {
        var store = provider.GetRequiredService<IMemoryStore>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var embedding = Vector(0, 0.1f);

        using (scopeAccessor.AsSystem("seed"))
        {
            await store.WriteAsync(
                new MemoryFactWrite(
                    MemoryScope.Global,
                    MemoryScopeKeys.GlobalSubject,
                    MemoryFactKind.Standing,
                    "policy",
                    "shared platform policy",
                    MemorySource.Chat,
                    "tester",
                    embedding),
                cancellationToken);
        }

        using (scopeAccessor.Begin(new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [])))
        {
            var results = await store.SearchAsync(
                new MemoryFactQuery(Scope: MemoryScope.Global, SubjectId: MemoryScopeKeys.GlobalSubject, Embedding: embedding),
                cancellationToken);

            results.ShouldHaveSingleItem().Text.ShouldBe("shared platform policy");
        }
    }

    /// <summary>A deterministic 768-dim vector: the basis axis plus an optional tilt into the next axis.</summary>
    private static float[] Vector(int tiltAxis, float tiltAmount)
    {
        var vector = new float[MemoryFactPolicy.EmbeddingDimensions];
        vector[0] = 1f;
        vector[tiltAxis + 1] = tiltAmount;
        return vector;
    }
}
