using System.Net;
using System.Text;
using System.Text.Json;
using Comuki.Modules.Knowledge.Infrastructure.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="OpenAIEmbeddingClient"/>: the API-key
/// guard rejects empty input at construction, the wire shape round-trips
/// through a substituted <see cref="HttpMessageHandler"/>, and the
/// dimension contract surfaces mismatches as a single, stable error.
/// </summary>
public sealed class OpenAIEmbeddingClientShould
{
    private const string ApiKey = "test-key";
    private const string Model = "text-embedding-3-small";
    private const int Dimensions = 4;

    [Fact(DisplayName = "Given an empty API key, when the constructor runs, then InvalidOperationException is thrown")]
    public void ThrowsOnEmptyApiKey()
    {
        var handler = new StubHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/"),
        };

        Should.Throw<InvalidOperationException>(() =>
            new OpenAIEmbeddingClient(client, apiKey: "  ", Model, Dimensions, NullLogger<OpenAIEmbeddingClient>.Instance));
    }

    [Fact(DisplayName = "Given construction succeeds, when ProviderName is read, then it is \"openai\"")]
    public void ProviderNameIsOpenAi()
    {
        var client = NewClient(handler: new StubHandler());

        client.ProviderName.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given a 200 OK with one 4-dim embedding, when EmbedAsync is called, then the returned vector matches")]
    public async Task EmbedAsyncReturnsVectorFromResponseAsync()
    {
        var handler = new StubHandler
        {
            Respond = static _ => JsonResponse(/*lang=json,strict*/ """{"data":[{"embedding":[0.1,0.2,0.3,0.4]}]}"""),
        };

        var client = NewClient(handler);

        var vector = await client.EmbedAsync("hello", TestContext.Current.CancellationToken);

        vector.Length.ShouldBe(Dimensions);
        vector[0].ShouldBe(0.1f);
        vector[3].ShouldBe(0.4f);
    }

    [Fact(DisplayName = "Given a 200 OK with two 4-dim embeddings, when EmbedBatchAsync is called, then both vectors arrive in order")]
    public async Task EmbedBatchAsyncReturnsAlignedVectorsAsync()
    {
        var handler = new StubHandler
        {
            Respond = static _ => JsonResponse(/*lang=json,strict*/ """{"data":[{"embedding":[0.1,0.2,0.3,0.4]},{"embedding":[-0.1,-0.2,-0.3,-0.4]}]}"""),
        };

        var client = NewClient(handler);

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
        var handler = new StubHandler
        {
            Respond = _ => JsonResponse(/*lang=json,strict*/ """{"data":[{"embedding":[0.1,0.2,0.3]}]}"""),
        };

        var client = NewClient(handler);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await client.EmbedBatchAsync(["hello"], TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("dimensions");
        exception.Message.ShouldContain(Dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact(DisplayName = "Given an outgoing POST, when the request body is captured, then it carries model + input under the JSON wire keys")]
    public async Task EmbedBatchAsyncSendsExpectedRequestBodyAsync()
    {
        var handler = new StubHandler
        {
            Respond = static _ => JsonResponse(/*lang=json,strict*/ """{"data":[{"embedding":[0.1,0.2,0.3,0.4]},{"embedding":[0.1,0.2,0.3,0.4]}]}"""),
        };

        var client = NewClient(handler);

        await client.EmbedBatchAsync(["alpha", "beta"], TestContext.Current.CancellationToken);

        handler.LastBody.ShouldNotBeNullOrEmpty();
        var body = JsonDocument.Parse(handler.LastBody);
        body.RootElement.GetProperty("model").GetString().ShouldBe(Model);
        var input = body.RootElement.GetProperty("input");
        input.GetArrayLength().ShouldBe(2);
        input[0].GetString().ShouldBe("alpha");
        input[1].GetString().ShouldBe("beta");
    }

    private static OpenAIEmbeddingClient NewClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/"),
        };
        return new OpenAIEmbeddingClient(httpClient, ApiKey, Model, Dimensions, NullLogger<OpenAIEmbeddingClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// Recording <see cref="HttpMessageHandler"/> stub — captures the last
/// request body and returns a caller-provided response. No real network
/// is involved; this exists so the OpenAI client can be exercised
/// against a deterministic JSON-RPC surface.
/// </summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        static _ => new HttpResponseMessage(HttpStatusCode.OK);

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return Respond(request);
    }
}
