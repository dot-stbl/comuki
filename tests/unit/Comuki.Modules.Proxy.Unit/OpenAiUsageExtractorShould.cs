using Comuki.Modules.Proxy.Application.Extraction;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>OpenAI <c>/v1/chat/completions</c> response shape parsing.</summary>
public sealed class OpenAiUsageExtractorShould
{
    [Fact(DisplayName = "Given a JSON body with prompt_tokens + completion_tokens, when ExtractAsync runs, then a populated report is returned")]
    public async Task ExtractsUsageFromResponseAsync()
    {
        var extractor = new OpenAiUsageExtractor();
        var projectId = ProjectId.New();
        var occurredAt = DateTimeOffset.UtcNow;
        var body = /*lang=json,strict*/ """
        {
          "id": "chatcmpl-abc",
          "model": "gpt-4o-mini",
          "choices": [],
          "usage": { "prompt_tokens": 12, "completion_tokens": 34, "total_tokens": 46 }
        }
        """;

        var report = await extractor.ExtractAsync(body, projectId, occurredAt, TestContext.Current.CancellationToken);

        report.ShouldNotBeNull();
        report.Model.ShouldBe("gpt-4o-mini");
        report.InputTokens.ShouldBe(12);
        report.OutputTokens.ShouldBe(34);
        report.CostUsdMicros.ShouldBe(0);
        report.ProjectId.ShouldBe(projectId);
    }

    [Fact(DisplayName = "Given an empty body, when ExtractAsync runs, then null is returned")]
    public async Task EmptyBodyReturnsNullAsync()
    {
        var extractor = new OpenAiUsageExtractor();

        var report = await extractor.ExtractAsync(body: "", ProjectId.New(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        report.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a body without usage, when ExtractAsync runs, then null is returned")]
    public async Task MissingUsageReturnsNullAsync()
    {
        var extractor = new OpenAiUsageExtractor();
        var body = /*lang=json,strict*/ """
        { "id": "chatcmpl-abc", "model": "gpt-4o-mini", "choices": [] }
        """;

        var report = await extractor.ExtractAsync(body, ProjectId.New(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        report.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a body with non-JSON content, when ExtractAsync runs, then null is returned")]
    public async Task MalformedBodyReturnsNullAsync()
    {
        var extractor = new OpenAiUsageExtractor();

        var report = await extractor.ExtractAsync("not json", ProjectId.New(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        report.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the provider id property, when accessed, then 'openai' is returned")]
    public void ProviderIdIsOpenAi()
    {
        var extractor = new OpenAiUsageExtractor();
        extractor.ProviderId.ShouldBe("openai");
    }

    [Fact(DisplayName = "Given a body with zero tokens, when ExtractAsync runs, then null is returned")]
    public async Task ZeroTokensReturnsNullAsync()
    {
        var extractor = new OpenAiUsageExtractor();
        var body = /*lang=json,strict*/ """{ "model": "gpt-4o-mini", "usage": { "prompt_tokens": 0, "completion_tokens": 0 } }""";

        var report = await extractor.ExtractAsync(body, ProjectId.New(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        report.ShouldBeNull();
    }
}
