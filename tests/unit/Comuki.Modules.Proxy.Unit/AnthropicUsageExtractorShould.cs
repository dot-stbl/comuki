using Comuki.Modules.Proxy.Application.Extraction;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Proxy.Unit;

/// <summary>Anthropic <c>/v1/messages</c> response shape parsing.</summary>
public sealed class AnthropicUsageExtractorShould
{
    [Fact(DisplayName = "Given a JSON body with input_tokens + output_tokens, when ExtractAsync runs, then a populated report is returned")]
    public async Task ExtractsUsageFromResponseAsync()
    {
        var extractor = new AnthropicUsageExtractor();
        var projectId = ProjectId.New();
        var occurredAt = DateTimeOffset.UtcNow;
        var body = /*lang=json,strict*/ """
        {
          "id": "msg_abc",
          "model": "claude-sonnet-4",
          "content": [],
          "usage": { "input_tokens": 7, "output_tokens": 21 }
        }
        """;

        var report = await extractor.ExtractAsync(body, projectId, occurredAt, TestContext.Current.CancellationToken);

        report.ShouldNotBeNull();
        report.Model.ShouldBe("claude-sonnet-4");
        report.InputTokens.ShouldBe(7);
        report.OutputTokens.ShouldBe(21);
    }

    [Fact(DisplayName = "Given a body without usage, when ExtractAsync runs, then null is returned")]
    public async Task MissingUsageReturnsNullAsync()
    {
        var extractor = new AnthropicUsageExtractor();
        var body = /*lang=json,strict*/ """{ "id": "msg_abc", "model": "claude-sonnet-4", "content": [] }""";

        var report = await extractor.ExtractAsync(body, ProjectId.New(), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        report.ShouldBeNull();
    }

    [Fact(DisplayName = "Given the provider id property, when accessed, then 'anthropic' is returned")]
    public void ProviderIdIsAnthropic()
    {
        var extractor = new AnthropicUsageExtractor();
        extractor.ProviderId.ShouldBe("anthropic");
    }
}
