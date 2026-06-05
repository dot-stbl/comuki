using System.Reflection;
using Comuki.Platform.Worker.Translator;
using Comuki.Platform.Worker.Translator.Interfaces;
using Comuki.Platform.Worker.Translator.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Worker.Translator.Integration.PiCli;

/// <summary>
/// End-to-end integration test for the .NET ↔ subprocess ↔ parser chain.
/// Spawns <c>TestFakePi</c> (a stream-json fixture-emitter) via the real
/// <see cref="PiRunner"/>, pipes its stdout through <see cref="WorkerTranslator"/>
/// (which uses <see cref="StreamJsonParser"/>), and asserts that the typed
/// <see cref="PiEvent"/>s surface correctly.
///
/// This validates the same .NET code path that production pi traffic flows
/// through (the only difference is which executable is spawned). When the
/// container network is back and we have a real API key, swap
/// <see cref="ResolveTestFakePiPath"/> for <c>"pi"</c> in this test and
/// the same assertions hold against real pi output.
/// </summary>
public sealed class PiCliIntegrationShould
{
    [Fact]
    public async Task SpawnSubprocessPipeStdoutAndParseAsync()
    {
        // Arrange — wire up the real components, just point PiRunner at TestFakePi.
        var testFakePiPath = ResolveTestFakePiPath();
        File.Exists(testFakePiPath).ShouldBeTrue($"TestFakePi not found at {testFakePiPath}");

        IPiRunner runner = new PiRunner(testFakePiPath, NullLogger<PiRunner>.Instance);
        ITranslator translator = new WorkerTranslator(runner);

        // Act — run the full chain.
        var events = new List<PiEvent>();
        await foreach (var piEvent in translator.TranslateAsync("ignored", CancellationToken.None))
        {
            events.Add(piEvent);
        }

        // Assert — every modelled event type from the fixtures surfaced.
        events.ShouldContain(e => e is PiEvent.SystemEvent);
        events.ShouldContain(e => e is PiEvent.UserEvent);
        events.ShouldContain(e => e is PiEvent.AssistantTextEvent);
        events.ShouldContain(e => e is PiEvent.AssistantToolUseEvent);
        events.OfType<PiEvent.ResultEvent>().ShouldNotBeEmpty();
        events.OfType<PiEvent.ResultEvent>().Last().Result.ShouldBe("(fake pi done)");
    }

    [Fact]
    public async Task ExitCodeZeroMeansNoExceptionOnSuccessAsync()
    {
        // Regression: PiRunner should NOT throw when the process exits 0.
        // (It does throw on non-zero exit; covered by the failure path below.)
        var testFakePiPath = ResolveTestFakePiPath();
        IPiRunner runner = new PiRunner(testFakePiPath, NullLogger<PiRunner>.Instance);

        var lines = new List<string>();
        await foreach (var line in runner.RunAsync("ok prompt", CancellationToken.None))
        {
            lines.Add(line);
        }

        lines.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task NonZeroExitThrowsInvalidOperationExceptionAsync()
    {
        // Regression: when the spawned process exits non-zero, PiRunner surfaces
        // stderr + exit code via an exception (not silent).
        // We point PiRunner at a guaranteed-to-fail command: `dotnet` invoked
        // without a DLL — dotnet prints usage and exits non-zero.
        var bogus = "dotnet";
        IPiRunner runner = new PiRunner(bogus, NullLogger<PiRunner>.Instance);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            var lines = new List<string>();
            await foreach (var line in runner.RunAsync("prompt", CancellationToken.None))
            {
                lines.Add(line);
            }
        });
    }

    private static string ResolveTestFakePiPath()
    {
        // The Integration.PiCli project references the TestFakePi project, so the
        // TestFakePi assembly is loaded into the test process. Assembly.Location
        // gives us the .dll path; the .exe launcher sits next to it.
        var assembly = Assembly.Load(
            "Comuki.Platform.Worker.Translator.Integration.TestTools.TestFakePi");
        var dllPath = assembly.Location;
        var dir = Path.GetDirectoryName(dllPath)
            ?? throw new InvalidOperationException("Could not resolve TestFakePi directory");

        var exeName = "Comuki.Platform.Worker.Translator.Integration.TestTools.TestFakePi"
            + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        return Path.Combine(dir, exeName);
    }
}
