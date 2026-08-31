using System.Reflection;
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
        await foreach (var line in runner.RunAsync("ignored prompt", TestContext.Current.CancellationToken))
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
            await foreach (var line in runner.RunAsync("prompt", TestContext.Current.CancellationToken))
            {
            }
        });
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
