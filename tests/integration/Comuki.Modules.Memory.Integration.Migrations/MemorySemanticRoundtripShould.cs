using System.Text;
using Comuki.Modules.Memory.Application.Ports;
using Comuki.Modules.Memory.Domain.Facts;
using Comuki.Modules.Memory.Domain.Facts.Kinds;
using Comuki.Modules.Memory.Domain.Facts.Scopes;
using Comuki.Modules.Memory.Domain.Facts.Sources;
using Comuki.Modules.Memory.Infrastructure;
using Comuki.Modules.Memory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Modules.Memory.Integration.Migrations;

/// <summary>
/// The semantic roundtrip the brain tools produce: a fact written WITH
/// its text embedding is found again by a search whose QUERY embedding
/// comes from different words of the same vocabulary — through the real
/// pgvector cosine path the AlignMemoryEmbeddingTo1536 migration set up
/// (vector(1536) + ivfflat index). The embedder here is deterministic
/// bag-of-words: same text → same point, shared words → nearby points —
/// the property a real embedding model provides, minus the network.
/// </summary>
public sealed class MemorySemanticRoundtripShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    /// <summary>boundary: initialised in InitializeAsync before any test runs</summary>
    private ServiceProvider provider = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryPersistence(container.GetConnectionString());
        provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<MemoryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        await container.DisposeAsync();
    }

    [Fact(DisplayName = "Given an embedded fact, when a search embeds a phrased-differently query, then the fact is found through the cosine path")]
    public async Task FindTheEmbeddedFactBySemanticProximityAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;

        await store.WriteAsync(
            Write("deploy-prefs", "deploys always run through docker compose with a migrate step",
                TestEmbeddings.Embed("deploys always run through docker compose with a migrate step")),
            cancellationToken);
        await store.WriteAsync(
            Write("ui-prefs", "the dashboard theme prefers dark surfaces",
                TestEmbeddings.Embed("the dashboard theme prefers dark surfaces")),
            cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Global,
                SubjectId: "global",
                Embedding: TestEmbeddings.Embed("how do deploys run? docker compose step"),
                Limit: 2),
            cancellationToken);

        // the cosine path has no distance cutoff — the disjoint-vocabulary
        // fact rides along too; the assertion is the RANKING: same
        // vocabulary first, unrelated second
        results.Count.ShouldBe(2);
        results[0].TopicKey.ShouldBe("deploy-prefs");
        results[1].TopicKey.ShouldBe("ui-prefs");
    }

    [Fact(DisplayName = "Given an embedded fact, when the exact text is searched, then the identical query vector matches it first")]
    public async Task MatchExactTextFirstAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        const string text = "remember the port pool is 17000 to 17200";
        await store.WriteAsync(Write("port-pool", text, TestEmbeddings.Embed(text)), cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Global,
                SubjectId: "global",
                Embedding: TestEmbeddings.Embed(text)),
            cancellationToken);

        results.ShouldHaveSingleItem().TopicKey.ShouldBe("port-pool");
    }

    [Fact(DisplayName = "Given a fact written without an embedding, when a semantic search runs, then the fallback ranking still answers")]
    public async Task DegradeToFallbackForUnembeddedFactsAsync()
    {
        var store = Store;
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.WriteAsync(Write("legacy", "written before embeddings were wired"), cancellationToken);

        var results = await store.SearchAsync(
            new MemoryFactQuery(
                Scope: MemoryScope.Global,
                SubjectId: "global",
                Embedding: TestEmbeddings.Embed("legacy fact")),
            cancellationToken);

        results.ShouldHaveSingleItem().TopicKey.ShouldBe("legacy");
    }

    private IMemoryStore Store => provider.GetRequiredService<IMemoryStore>();

    private static MemoryFactWrite Write(string topicKey, string text, float[]? embedding = null)
    {
        return new MemoryFactWrite(
            MemoryScope.Global,
            "global",
            MemoryFactKind.Standing,
            topicKey,
            text,
            MemorySource.Chat,
            "brain",
            embedding);
    }
}

/// <summary>
/// Deterministic bag-of-words embedder at the policy dimension: every
/// word hashes (FNV-1a, stable across runs) onto one dimension with a
/// unit weight, the vector is then L2-normalized — identical texts land
/// on the same point, texts sharing words nearby, disjoint vocabularies
/// roughly orthogonal. Good enough to exercise the cosine path the way a
/// real model would, with zero network.
/// </summary>
file static class TestEmbeddings
{
    public static float[] Embed(string text)
    {
        var vector = new float[MemoryFactPolicy.EmbeddingDimensions];
        var words = 0;
        foreach (var word in text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            vector[Fnv1a(word) % vector.Length] += 1f;
            words++;
        }

        if (words == 0)
        {
            return vector;
        }

        var norm = 0f;
        for (var index = 0; index < vector.Length; index++)
        {
            norm += vector[index] * vector[index];
        }

        norm = MathF.Sqrt(norm);
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] /= norm;
        }

        return vector;
    }

    private static uint Fnv1a(string word)
    {
        var hash = 2166136261u;
        foreach (var character in Encoding.UTF8.GetBytes(word))
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }
}
