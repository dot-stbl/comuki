using System.Text.Json.Nodes;
using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// <see cref="PiExecutionEnvironment"/>: composes
/// <see cref="PiEnvironment.FromClaim"/> with
/// <see cref="PiCodingAgentDirectory"/>; returns a <c>null</c>
/// <see cref="PiExecutionEnvironment.Environment"/> when the claim carries
/// no proxy (byte-for-byte the same behavior as today), and adds a
/// <c>PI_CODING_AGENT_DIR</c> stamp plus a provisioned
/// <c>models.json</c> when it does. Cleans the temp directory up on
/// every exit path.
/// </summary>
public sealed class PiExecutionEnvironmentShould : IDisposable
{
    private readonly string? originalEnv;

    public PiExecutionEnvironmentShould()
    {
        // PiExecutionEnvironment.PrepareAsync reads PI_CODING_AGENT_DIR from the
        // process-wide env to decide what to copy in; clear it during these tests
        // so an image-shipped value on the dev machine cannot leak in and so the
        // provisioned directory lands in the test root, not somewhere else. xUnit
        // v3 parallelizes test classes by default — IDisposable restores the
        // original value on dispose so parallel siblings are not affected.
        originalEnv = Environment.GetEnvironmentVariable(PiCodingAgentDirectory.EnvironmentVariable);
        Environment.SetEnvironmentVariable(PiCodingAgentDirectory.EnvironmentVariable, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PiCodingAgentDirectory.EnvironmentVariable, originalEnv);
    }

    [Fact(DisplayName = "Given a claim without proxy fields, when PrepareAsync runs, then the environment is null and no directory is left behind")]
    public async Task PrepareAsyncReturnsNullEnvironmentAndNoDirectoryWhenClaimHasNoProxyAsync()
    {
        var claimed = NewClaim(ProxyBaseUrl: null, VirtualKey: null);
        var root = Path.Combine(Path.GetTempPath(), $"pee-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var before = Directory.EnumerateDirectories(root).ToList();

        var environment = await PiExecutionEnvironment.PrepareAsync(claimed, root, TestContext.Current.CancellationToken);

        environment.Environment.ShouldBeNull();
        await environment.DisposeAsync();
        Directory.EnumerateDirectories(root).ToList().ShouldBe(before, "no directory should be provisioned when the claim carries no proxy");
    }

    [Fact(DisplayName = "Given a claim with proxy fields, when PrepareAsync runs, then the environment carries the proxy stamp plus PI_CODING_AGENT_DIR pointing at a valid models.json; after DisposeAsync, that directory is gone")]
    public async Task PrepareAsyncStampsPiCodingAgentDirAndCleansUpOnDisposeAsync()
    {
        var claimed = NewClaim(ProxyBaseUrl: "https://comuki-proxy:17080", VirtualKey: "minted_token_xyz");
        var root = Path.Combine(Path.GetTempPath(), $"pee-{Guid.NewGuid():N}");

        var environment = await PiExecutionEnvironment.PrepareAsync(claimed, root, TestContext.Current.CancellationToken);
        try
        {
            environment.Environment.ShouldNotBeNull();
            environment.Environment!.Count.ShouldBe(3);
            environment.Environment[PiEnvironment.AnthropicBaseUrlVariable].ShouldBe("https://comuki-proxy:17080");
            environment.Environment[PiEnvironment.AnthropicAuthTokenVariable].ShouldBe("minted_token_xyz");
            var piCodingAgentDir = environment.Environment[PiCodingAgentDirectory.EnvironmentVariable];
            Directory.Exists(piCodingAgentDir).ShouldBeTrue();
            var parsed = JsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(piCodingAgentDir, PiCodingAgentDirectory.ModelsJsonFileName),
                TestContext.Current.CancellationToken))!.AsObject();
            parsed["providers"]!["anthropic"]!["baseUrl"]!.GetValue<string>().ShouldBe("https://comuki-proxy:17080");
        }
        finally
        {
            await environment.DisposeAsync();
        }

        // Re-derive the directory from the captured dictionary since the
        // disposable may have already cleared its internal field on the way out.
        Directory.Exists(environment.Environment![PiCodingAgentDirectory.EnvironmentVariable]).ShouldBeFalse();
    }

    private static ClaimedWorkItemResponse NewClaim(string? ProxyBaseUrl = null, string? VirtualKey = null)
    {
        return new ClaimedWorkItemResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "implement",
            "do it",
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds(),
            1,
            1,
            ProxyBaseUrl,
            VirtualKey);
    }
}
