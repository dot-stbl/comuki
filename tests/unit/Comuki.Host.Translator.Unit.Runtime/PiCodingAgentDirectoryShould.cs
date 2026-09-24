using System.Text.Json.Nodes;
using Comuki.Host.Translator.Execution;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// <see cref="PiCodingAgentDirectory"/>: writes a fresh per-execution
/// directory whose <c>models.json</c> carries
/// <c>providers.anthropic.baseUrl</c> set to the claim's proxy, merges
/// (does not clobber) any image-shipped <c>models.json</c> fields, never
/// writes a virtual key to disk in any field, and removes the directory
/// on <see cref="PiCodingAgentDirectory.Cleanup"/>.
/// </summary>
public sealed class PiCodingAgentDirectoryShould
{
    private const string ProxyUrl = "https://comuki-proxy:17080";

    [Fact(DisplayName = "Given an empty source and a proxy URL, when ProvisionAsync runs, then models.json writes only the anthropic baseUrl override")]
    public async Task WriteFreshAnthropicOverrideWhenNoExistingConfigAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pcad-{Guid.NewGuid():N}");

        try
        {
            var directory = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, cancellationToken: TestContext.Current.CancellationToken);

            var modelsJsonPath = Path.Combine(directory, PiCodingAgentDirectory.ModelsJsonFileName);
            File.Exists(modelsJsonPath).ShouldBeTrue();
            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(modelsJsonPath, TestContext.Current.CancellationToken))!.AsObject();
            parsed["providers"]!["anthropic"]!["baseUrl"]!.GetValue<string>().ShouldBe(ProxyUrl);
        }
        finally
        {
            PiCodingAgentDirectory.Cleanup(root);
        }
    }

    [Fact(DisplayName = "Given an existing config with an unrelated provider, an anthropic.compat, and a top-level key, when ProvisionAsync runs, then every other field survives and baseUrl is overwritten")]
    public async Task MergeOverBaseUrlAndKeepEverythingElseUntouchedAsync()
    {
        var existing = Directory.CreateTempSubdirectory("pcad-existing-");
        await File.WriteAllTextAsync(
            Path.Combine(existing.FullName, PiCodingAgentDirectory.ModelsJsonFileName),
                                 /*lang=json,strict*/
                                 """
            {
              "theme": "dark",
              "providers": {
                "openai": { "baseUrl": "https://api.openai.com/v1" },
                "anthropic": {
                  "baseUrl": "https://stale.example.com",
                  "compat": { "supportsStrictTools": true }
                }
              }
            }
            """,
            TestContext.Current.CancellationToken);
        var root = Path.Combine(Path.GetTempPath(), $"pcad-{Guid.NewGuid():N}");

        try
        {
            var directory = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, existing.FullName, TestContext.Current.CancellationToken);

            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, PiCodingAgentDirectory.ModelsJsonFileName),
                TestContext.Current.CancellationToken))!.AsObject();

            parsed["theme"]!.GetValue<string>().ShouldBe("dark");
            parsed["providers"]!["openai"]!["baseUrl"]!.GetValue<string>().ShouldBe("https://api.openai.com/v1");
            parsed["providers"]!["anthropic"]!["baseUrl"]!.GetValue<string>().ShouldBe(ProxyUrl);
            parsed["providers"]!["anthropic"]!["compat"]!["supportsStrictTools"]!.GetValue<bool>().ShouldBeTrue();
        }
        finally
        {
            PiCodingAgentDirectory.Cleanup(root);
            existing.Delete(recursive: true);
        }
    }

    [Fact(DisplayName = "Given a provisioned directory, when read, then no file contains a sample virtual-key token or a baked apiKey")]
    public async Task WriteNoTokenAndNoApiKeyToDiskAsync()
    {
        var sensitiveToken = "sk-token-must-not-appear-anywhere-on-disk-9876543210";

        // Stage the token in process env under the var name pi reads auth from so
        // any future bug that accidentally reads ANTHROPIC_AUTH_TOKEN while writing
        // models.json surfaces as a failing test, not a credential leak on disk.
        var previousToken = Environment.GetEnvironmentVariable(PiEnvironment.AnthropicAuthTokenVariable);
        Environment.SetEnvironmentVariable(PiEnvironment.AnthropicAuthTokenVariable, sensitiveToken);
        var root = Path.Combine(Path.GetTempPath(), $"pcad-{Guid.NewGuid():N}");
        try
        {
            var directory = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, cancellationToken: TestContext.Current.CancellationToken);

            var modelsJson = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, PiCodingAgentDirectory.ModelsJsonFileName),
                TestContext.Current.CancellationToken))!.AsObject();
            modelsJson.ContainsKey("apiKey").ShouldBeFalse();

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).ShouldNotContain(sensitiveToken);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(PiEnvironment.AnthropicAuthTokenVariable, previousToken);
            PiCodingAgentDirectory.Cleanup(root);
        }
    }

    [Fact(DisplayName = "Given a provisioned directory, when Cleanup runs, then the directory no longer exists; null and missing paths do not throw")]
    public async Task CleanupRemovesDirectoryAndToleratesBadInputsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pcad-{Guid.NewGuid():N}");

        var directory = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, cancellationToken: TestContext.Current.CancellationToken);
        Directory.Exists(directory).ShouldBeTrue();

        PiCodingAgentDirectory.Cleanup(directory);
        Directory.Exists(directory).ShouldBeFalse();

        Should.NotThrow(() => PiCodingAgentDirectory.Cleanup(null));
        Should.NotThrow(() => PiCodingAgentDirectory.Cleanup(string.Empty));
        Should.NotThrow(() => PiCodingAgentDirectory.Cleanup(Path.Combine(root, "never-existed")));
    }

    [Fact(DisplayName = "Given a null or non-existent existingAgentDirectory, when ProvisionAsync runs, then models.json still gets the anthropic override without throwing")]
    public async Task TolerateNullAndMissingExistingDirectoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pcad-{Guid.NewGuid():N}");

        try
        {
            var fromNull = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, existingAgentDirectory: null, cancellationToken: TestContext.Current.CancellationToken);
            var parsedFromNull = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(fromNull, PiCodingAgentDirectory.ModelsJsonFileName),
                TestContext.Current.CancellationToken))!.AsObject();
            parsedFromNull["providers"]!["anthropic"]!["baseUrl"]!.GetValue<string>().ShouldBe(ProxyUrl);

            var missing = Path.Combine(root, "definitely-not-here");
            var fromMissing = await PiCodingAgentDirectory.ProvisionAsync(ProxyUrl, root, missing, TestContext.Current.CancellationToken);
            var parsedFromMissing = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(fromMissing, PiCodingAgentDirectory.ModelsJsonFileName),
                TestContext.Current.CancellationToken))!.AsObject();
            parsedFromMissing["providers"]!["anthropic"]!["baseUrl"]!.GetValue<string>().ShouldBe(ProxyUrl);
        }
        finally
        {
            PiCodingAgentDirectory.Cleanup(root);
        }
    }
}
