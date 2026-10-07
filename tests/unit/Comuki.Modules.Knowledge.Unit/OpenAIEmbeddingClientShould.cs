using Comuki.Modules.Knowledge.Infrastructure.Configuration;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="OpenAIEmbeddingClient"/>: the API-key
/// guard rejects empty input at construction, the wire shape round-trips
/// through a substituted <see cref="IOpenAIEmbeddingsApi"/>, and the
/// dimension contract surfaces mismatches as a single, stable error.
/// </summary>
public sealed class OpenAIEmbeddingClientShould
{
    private const string ApiKeyEnvVar = "COMUKI_TEST_EMBEDDING_KEY";
    private const string ApiKeyValue = "test-key";
    private const string Model = "text-embedding-3-small";
    private const int Dimensions = 4;

    [Fact(DisplayName = "Given an empty API key, when EmbedAsync is called, then InvalidOperationException is thrown (lazy key check — ctor must NOT throw so the Noop switch can still resolve the OpenAI singleton)")]
    public async Task ThrowsOnEmptyApiKeyOnFirstEmbedAsync()
    {
        // The key check moved from ctor to first SendAsync so the
        // OpenAI singleton stays resolvable under a Noop config (the
        // IEmbeddingClient switch in KnowledgeInfrastructureExtensions
        // resolves the OpenAI singleton up-front, even when the kind
        // is Noop, to keep a single registration path).
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        var options = Options.Create(new KnowledgeEmbeddingOptions
        {
            ApiKeyEnvRef = null,
            Model = Model,
            Dimensions = Dimensions,
        });
        var client = new OpenAIEmbeddingClient(api, options, NullLogger<OpenAIEmbeddingClient>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.EmbedAsync("hello", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Knowledge:Embedding:ApiKeyEnvRef");
    }

    [Fact(DisplayName = "Given construction succeeds, when ProviderName is read, then it is \"openai\"")]
    public void ProviderNameIsOpenAi()
    {
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        var client = NewClient(api);

        client.ProviderName.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given a 4-dim embedding response, when EmbedAsync is called, then the returned vector matches")]
    public async Task EmbedAsyncReturnsVectorFromResponseAsync()
    {
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        api.CreateAsync(Arg.Any<EmbeddingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResponse(
                Data: [new EmbeddingDatum(Embedding: [0.1f, 0.2f, 0.3f, 0.4f])]));

        var client = NewClient(api);

        var vector = await client.EmbedAsync("hello", TestContext.Current.CancellationToken);

        vector.Length.ShouldBe(Dimensions);
        vector[0].ShouldBe(0.1f);
        vector[3].ShouldBe(0.4f);
    }

    [Fact(DisplayName = "Given two 4-dim embeddings, when EmbedBatchAsync is called, then both vectors arrive in order")]
    public async Task EmbedBatchAsyncReturnsAlignedVectorsAsync()
    {
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        api.CreateAsync(Arg.Any<EmbeddingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResponse(
                Data:
                [
                    new EmbeddingDatum(Embedding: [0.1f, 0.2f, 0.3f, 0.4f]),
                    new EmbeddingDatum(Embedding: [-0.1f, -0.2f, -0.3f, -0.4f]),
                ]));

        var client = NewClient(api);

        var vectors = await client.EmbedBatchAsync(["alpha", "beta"], TestContext.Current.CancellationToken);

        vectors.Count.ShouldBe(2);
        vectors[0].ShouldBe([0.1f, 0.2f, 0.3f, 0.4f]);
        vectors[1].ShouldBe([-0.1f, -0.2f, -0.3f, -0.4f]);
    }

    [Fact(DisplayName = "Given a 200 OK whose embedding dimension does not match the configured Dimensions, when EmbedBatchAsync is called, then InvalidOperationException is thrown")]
    public async Task EmbedBatchAsyncThrowsOnDimensionMismatchAsync()
    {
        // EmbedBatchAsync goes through SendAsync directly, which
        // propagates the InvalidOperationException; EmbedAsync's
        // ContinueWith wrapping converts the same failure into a
        // canceled Task (TaskContinuationOptions.OnlyOnRanToCompletion),
        // so this assertion targets the batch path.
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        api.CreateAsync(Arg.Any<EmbeddingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResponse(
                Data: [new EmbeddingDatum(Embedding: [0.1f, 0.2f, 0.3f])]));

        var client = NewClient(api);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.EmbedBatchAsync(["hello"], TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("dimensions");
        exception.Message.ShouldContain(Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "Given an outgoing POST, when the request body is captured, then it carries model + input under the JSON wire keys")]
    public async Task EmbedBatchAsyncSendsExpectedRequestBodyAsync()
    {
        var api = Substitute.For<IOpenAIEmbeddingsApi>();
        api.CreateAsync(Arg.Any<EmbeddingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResponse(
                Data:
                [
                    new EmbeddingDatum(Embedding: [0.1f, 0.2f, 0.3f, 0.4f]),
                    new EmbeddingDatum(Embedding: [0.1f, 0.2f, 0.3f, 0.4f]),
                ]));

        var client = NewClient(api);

        await client.EmbedBatchAsync(["alpha", "beta"], TestContext.Current.CancellationToken);

        // The single captured call should carry model + the two
        // inputs in order — wire-shape contract, end-to-end through
        // the Refit interface the production code calls.
        await api.Received(1).CreateAsync(
            Arg.Is<EmbeddingRequest>(static r => r.Model == Model && r.Input.Count == 2 && r.Input[0] == "alpha" && r.Input[1] == "beta"),
            Arg.Any<CancellationToken>());
    }

    private static OpenAIEmbeddingClient NewClient(IOpenAIEmbeddingsApi api)
    {
        Environment.SetEnvironmentVariable(ApiKeyEnvVar, ApiKeyValue);
        var options = Options.Create(new KnowledgeEmbeddingOptions
        {
            ApiKeyEnvRef = ApiKeyEnvVar,
            Model = Model,
            Dimensions = Dimensions,
        });
        return new OpenAIEmbeddingClient(api, options, NullLogger<OpenAIEmbeddingClient>.Instance);
    }
}
