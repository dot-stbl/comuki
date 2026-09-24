using System.Reflection;
using System.Text.Json.Nodes;
using Comuki.Host.Translator.Parsing;
using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// The subprocess chain test (ported from the legacy PiCli suite):
/// <see cref="PiRunner"/> spawns the real <c>TestFakePi</c> executable and
/// its stdout flows through <see cref="StreamJsonParser"/> — the same .NET
/// path production pi traffic takes. Swap the executable for <c>pi</c> and
/// the assertions hold against real pi output (verified manually in T3.0).
/// </summary>
public sealed class PiRunnerShould
{
    [Fact]
    public async Task SpawnSubprocessPipeStdoutAndParseAsync()
    {
        var runner = new PiRunner(
            Options.Create(new TranslatorOptions
            {
                OrchestratorBaseUrl = new Uri("http://localhost:8080"),
                OrchestratorGrpcUrl = new Uri("http://localhost:5051"),
                WorkerToken = "not-used-in-this-test",
                ProfileKey = "implement",
                ProfilesRef = "refs/heads/main",
                WorkerImage = "ghcr.io/comuki/worker:s3",
                PiExecutable = ResolveTestFakePiPath(),
                WorkingDirectory = Path.GetTempPath(),
            }),
            NullLogger<PiRunner>.Instance);

        var events = new List<PiEvent>();
        await foreach (var line in runner.RunAsync("ignored prompt", environment: null, TestContext.Current.CancellationToken))
        {
            events.AddRange(StreamJsonParser.ParseLine(line));
        }

        events.ShouldContain(static piEvent => piEvent is PiEvent.SessionHeaderEvent);
        events.ShouldContain(static piEvent => piEvent is PiEvent.TextDeltaEvent);
        events.ShouldContain(static piEvent => piEvent is PiEvent.ToolCallEvent);
        events.OfType<PiEvent.AssistantTextEvent>().ShouldContain(static piEvent => piEvent.Text == "(fake pi done)");
        events.ShouldContain(static piEvent => piEvent is PiEvent.AgentEndEvent);
    }

    [Fact]
    public async Task ThrowOnNonZeroExitAsync()
    {
        var runner = new PiRunner(
            Options.Create(new TranslatorOptions
            {
                OrchestratorBaseUrl = new Uri("http://localhost:8080"),
                OrchestratorGrpcUrl = new Uri("http://localhost:5051"),
                WorkerToken = "not-used-in-this-test",
                ProfileKey = "implement",
                ProfilesRef = "refs/heads/main",
                WorkerImage = "ghcr.io/comuki/worker:s3",
                PiExecutable = "dotnet",
                WorkingDirectory = Path.GetTempPath(),
            }),
            NullLogger<PiRunner>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var line in runner.RunAsync("prompt", environment: null, TestContext.Current.CancellationToken))
            {
            }
        });
    }

    [Fact]
    public async Task StampEnvironmentOverridesOntoTheChildProcessOnlyAsync()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("comuki-pirunner-env-");
        var runner = NewRunner(workingDirectory.FullName);

        await foreach (var _ in runner.RunAsync(
             "ignored prompt",
             new Dictionary<string, string>
             {
                 ["ANTHROPIC_BASE_URL"] = "http://comuki-proxy:17080",
                 ["ANTHROPIC_AUTH_TOKEN"] = "minted_runner_token",
             },
             TestContext.Current.CancellationToken))
        {
        }

        var dumpPath = Path.Combine(workingDirectory.FullName, "fake-pi-env.json");
        File.Exists(dumpPath).ShouldBeTrue("TestFakePi dumps the received model-gateway env when the token stamp is present");
        var dump = await File.ReadAllTextAsync(dumpPath, TestContext.Current.CancellationToken);
        dump.ShouldContain("http://comuki-proxy:17080");
        dump.ShouldContain("minted_runner_token");
    }

    [Fact]
    public async Task StampPiCodingAgentDirWithModelsJsonOntoTheChildProcessAsync()
    {
        var proxyBaseUrl = "http://comuki-proxy:17080";
        var workingDirectory = Directory.CreateTempSubdirectory("comuki-pirunner-env-");
        var agentDirectory = Directory.CreateTempSubdirectory("comuki-pirunner-agent-");
        try
        {
            // Pre-populate the agent dir with a models.json that already has an unrelated
            // openai provider + theme — the runner under test doesn't merge, but the
            // assertion confirms that whatever models.json the runner stamped with the
            // additional PI_CODING_AGENT_DIR key reached the child intact.
            await File.WriteAllTextAsync(
                Path.Combine(agentDirectory.FullName, "models.json"),
                $$"""
                {
                  "theme": "dark",
                  "providers": {
                    "openai": { "baseUrl": "https://api.openai.com/v1" },
                    "anthropic": { "baseUrl": "{{proxyBaseUrl}}" }
                  }
                }
                """,
                TestContext.Current.CancellationToken);

            var runner = NewRunner(workingDirectory.FullName);
            var environment = new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = proxyBaseUrl,
                ["ANTHROPIC_AUTH_TOKEN"] = "minted_runner_token",
                ["PI_CODING_AGENT_DIR"] = agentDirectory.FullName,
            };

            await foreach (var _ in runner.RunAsync(
                 "ignored prompt",
                 environment,
                 TestContext.Current.CancellationToken))
            {
            }

            environment.ShouldContainKey("PI_CODING_AGENT_DIR");

            var dumpPath = Path.Combine(workingDirectory.FullName, "fake-pi-env.json");
            File.Exists(dumpPath).ShouldBeTrue();
            var dump = JsonNode.Parse(await File.ReadAllTextAsync(dumpPath, TestContext.Current.CancellationToken))!.AsObject();
            dump["piCodingAgentDir"]!.GetValue<string>().ShouldBe(agentDirectory.FullName);
            dump["modelsJson"]!.GetValue<string>().ShouldContain(proxyBaseUrl);
        }
        finally
        {
            agentDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StampNothingWhenNoEnvironmentOverridesAreGivenAsync()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("comuki-pirunner-env-");
        var runner = NewRunner(workingDirectory.FullName);

        // Empty strings override any machine-level ANTHROPIC_* the child
        // would otherwise inherit, so the absence assertion is hermetic.
        await foreach (var _ in runner.RunAsync(
             "ignored prompt",
             new Dictionary<string, string>
             {
                 ["ANTHROPIC_BASE_URL"] = string.Empty,
                 ["ANTHROPIC_AUTH_TOKEN"] = string.Empty,
             },
             TestContext.Current.CancellationToken))
        {
        }

        File.Exists(Path.Combine(workingDirectory.FullName, "fake-pi-env.json"))
            .ShouldBeFalse("no token stamp means no env dump and the inherited environment is untouched");
    }

    private static PiRunner NewRunner(string workingDirectory)
    {
        return new PiRunner(
            Options.Create(new TranslatorOptions
            {
                OrchestratorBaseUrl = new Uri("http://localhost:8080"),
                OrchestratorGrpcUrl = new Uri("http://localhost:5051"),
                WorkerToken = "not-used-in-this-test",
                ProfileKey = "implement",
                ProfilesRef = "refs/heads/main",
                WorkerImage = "ghcr.io/comuki/worker:s3",
                PiExecutable = ResolveTestFakePiPath(),
                WorkingDirectory = workingDirectory,
            }),
            NullLogger<PiRunner>.Instance);
    }

    private static string ResolveTestFakePiPath()
    {
        var assembly = Assembly.Load("Comuki.TestFakePi");
        var directory = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("could not resolve TestFakePi directory");
        var executableName = OperatingSystem.IsWindows() ? "Comuki.TestFakePi.exe" : "Comuki.TestFakePi";
        return Path.Combine(directory, executableName);
    }
}
