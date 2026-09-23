namespace Comuki.AgentTest.Runner.Scenarios;

/// <summary>
/// Claim labels the runner stamps on <c>ComputeStartRequest</c> (and the
/// matching queued <c>WorkItem</c> row) plus the T2a-only TestFakePi
/// fixture selection.
/// </summary>
public sealed record ScenarioWorker
{
    /// <summary>Worker image tag the container is provisioned from (claim label — must equal <c>COMUKI_WORKER_IMAGE</c>).</summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>Profile key claim label.</summary>
    public string ProfileKey { get; init; } = "implement";

    /// <summary>Pinned profiles-git-ref claim label.</summary>
    public string ProfilesRef { get; init; } = "test";

    /// <summary>
    /// Relative path (under the scenario file's directory) to a directory of
    /// pi-native stream-json fixture files TestFakePi should stream. T2a
    /// only — TestFakePi has no seam to receive <c>--fixtures-dir</c> from a
    /// real claim (<c>Comuki.Host.Translator.Runtime.PiRunner</c> always
    /// spawns it bare), so this is honored by baking the directory into the
    /// test worker image at build time, not by an argument. Empty =
    /// TestFakePi's own bundled default fixtures.
    /// </summary>
    public string? PiFixturesDir { get; init; }

    /// <summary>Forced TestFakePi exit code — 0 (success) unless set; a scenario proving a failure path sets this.</summary>
    public int? PiExitCode { get; init; }
}
