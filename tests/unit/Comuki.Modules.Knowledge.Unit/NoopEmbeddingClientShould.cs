using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="NoopEmbeddingClient"/>: the deterministic
/// random-vector embedder must report the requested dimensionality, stay
/// stable across calls for the same text, and reject out-of-range
/// dimensions at construction.
/// </summary>
public sealed class NoopEmbeddingClientShould
{
    [Fact(DisplayName = "Given construction with 1536 dimensions, when ProviderName is read, then it is \"noop\"")]
    public void ProviderNameIsNoop()
    {
        var client = new NoopEmbeddingClient(EmbeddingSql.Dimensions);

        client.ProviderName.ShouldBe("noop");
    }

    [Theory(DisplayName = "Given an out-of-range dimension, when the constructor is called, then ArgumentOutOfRangeException is thrown")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    public void ThrowsOnOutOfRangeDimensions(int dimensions)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new NoopEmbeddingClient(dimensions));
    }

    [Fact(DisplayName = "Given a text, when EmbedAsync is called twice, then the two vectors are element-wise equal")]
    public async Task EmbedAsyncIsDeterministicAsync()
    {
        var client = new NoopEmbeddingClient(EmbeddingSql.Dimensions);

        var first = await client.EmbedAsync("hello world", TestContext.Current.CancellationToken);
        var second = await client.EmbedAsync("hello world", TestContext.Current.CancellationToken);

        first.Length.ShouldBe(second.Length);
        first.Length.ShouldBe(EmbeddingSql.Dimensions);
        for (var index = 0; index < first.Length; index++)
        {
            first[index].ShouldBe(second[index]);
        }
    }

    [Fact(DisplayName = "Given two different texts, when EmbedAsync is called, then the vectors differ in at least one component")]
    public async Task EmbedAsyncProducesDistinctVectorsForDistinctInputsAsync()
    {
        var client = new NoopEmbeddingClient(EmbeddingSql.Dimensions);

        var alpha = await client.EmbedAsync("alpha", TestContext.Current.CancellationToken);
        var beta = await client.EmbedAsync("beta", TestContext.Current.CancellationToken);

        var anyEqual = false;
        for (var index = 0; index < alpha.Length; index++)
        {
            if (alpha[index] == beta[index])
            {
                anyEqual = true;
                break;
            }
        }

        anyEqual.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a custom dimensionality, when EmbedAsync is called, then the returned vector has that length")]
    public async Task EmbedAsyncRespectsCustomDimensionsAsync()
    {
        var client = new NoopEmbeddingClient(64);

        var vector = await client.EmbedAsync("anything", TestContext.Current.CancellationToken);

        vector.Length.ShouldBe(64);
    }

    [Fact(DisplayName = "Given a batch of three texts, when EmbedBatchAsync is called, then it returns three vectors in the same order")]
    public async Task EmbedBatchAsyncReturnsAlignedVectorsAsync()
    {
        var client = new NoopEmbeddingClient(EmbeddingSql.Dimensions);

        var inputs = new[] { "alpha", "beta", "gamma" };
        var expected = await client.EmbedAsync("beta", TestContext.Current.CancellationToken);

        var vectors = await client.EmbedBatchAsync(inputs, TestContext.Current.CancellationToken);

        vectors.Count.ShouldBe(inputs.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            vectors[1][index].ShouldBe(expected[index]);
        }
    }
}
