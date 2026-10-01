using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// <see cref="TranslatorEnvironment.Snapshot"/> contract (issue #151):
/// the snapshot must OMIT every entry whose <c>COMUKI_*</c> env var is
/// unset, never write <c>null</c>. A <c>null</c> in the in-memory config
/// would overwrite <c>TranslatorOptions</c>' declared defaults — the
/// pre-#151 bug crashed <c>ProfilesProvider.PrepareAsync</c> exactly
/// that way. Each test runs in a fresh class instance (xUnit v3
/// per-test instantiation); the constructor snapshots every entry the
/// helper reads, the test runs against a deterministic env, and the
/// disposables restore every original value so parallel siblings see
/// the same state they would have if these tests did not exist.
/// Serialised through <see cref="TranslatorEnvSafeCollection"/> so the
/// env-mutating tests in this class do not race
/// <see cref="ProfilesProviderShould.SnapshotWithUnsetWorkingDirectoryPreservesDefault"/>.
/// </summary>
[Collection(nameof(TranslatorEnvSafeCollection))]
public sealed class TranslatorEnvironmentSnapshotShould : IDisposable
{
    /// <summary>The ten <c>COMUKI_*</c> env vars the helper reads (must stay in lockstep with <see cref="TranslatorEnvironment"/>).</summary>
    private static readonly string[] envVarNames =
    [
        "COMUKI_ORCH_HTTP",
        "COMUKI_ORCH_GRPC",
        "COMUKI_WORKER_TOKEN",
        "COMUKI_PROFILE_KEY",
        "COMUKI_PROFILES_REF",
        "COMUKI_WORKER_IMAGE",
        "COMUKI_PROFILES_PATH",
        "COMUKI_PROFILES_GIT_URL",
        "COMUKI_PI_EXECUTABLE",
        "COMUKI_WORKING_DIRECTORY",
    ];

    private readonly string?[] originalValues;

    public TranslatorEnvironmentSnapshotShould()
    {
        originalValues = new string?[envVarNames.Length];
        for (var i = 0; i < envVarNames.Length; i++)
        {
            originalValues[i] = Environment.GetEnvironmentVariable(envVarNames[i]);
            Environment.SetEnvironmentVariable(envVarNames[i], null);
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < envVarNames.Length; i++)
        {
            Environment.SetEnvironmentVariable(envVarNames[i], originalValues[i]);
        }
    }

    [Fact(DisplayName = "Given no COMUKI_* env vars set, when Snapshot is called, then the returned dictionary is empty (the null-binding footgun stays foreclosed)")]
    public void EmptyEnvironmentProducesEmptySnapshot()
    {
        var snapshot = TranslatorEnvironment.Snapshot();

        snapshot.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given only COMUKI_WORKING_DIRECTORY is set, when Snapshot is called, then exactly that one entry is present with the right key and value")]
    public void SingleEnvVarProducesSingleEntry()
    {
        const string workingDirectory = "/work";
        Environment.SetEnvironmentVariable("COMUKI_WORKING_DIRECTORY", workingDirectory);

        var snapshot = TranslatorEnvironment.Snapshot();

        snapshot.Count.ShouldBe(1);
        snapshot.ShouldContainKey("Translator:WorkingDirectory");
        snapshot["Translator:WorkingDirectory"].ShouldBe(workingDirectory);
    }

    [Fact(DisplayName = "Given all ten COMUKI_* env vars are set, when Snapshot is called, then the returned dictionary has ten entries with the expected keys and values, and none is null")]
    public void FullEnvironmentProducesTenNonNullEntries()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["COMUKI_ORCH_HTTP"] = "https://orchestrator.example",
            ["COMUKI_ORCH_GRPC"] = "https://orchestrator.example:17004",
            ["COMUKI_WORKER_TOKEN"] = "0123456789abcdef0123456789abcdef",
            ["COMUKI_PROFILE_KEY"] = "implement",
            ["COMUKI_PROFILES_REF"] = "refs/tags/v1.2",
            ["COMUKI_WORKER_IMAGE"] = "ghcr.io/example/worker@sha256:abc",
            ["COMUKI_PROFILES_PATH"] = "/etc/comuki/profiles",
            ["COMUKI_PROFILES_GIT_URL"] = "https://github.com/example/profiles",
            ["COMUKI_PI_EXECUTABLE"] = "/usr/local/bin/pi",
            ["COMUKI_WORKING_DIRECTORY"] = "/work",
        };
        foreach (var pair in expected)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        var snapshot = TranslatorEnvironment.Snapshot();

        snapshot.Count.ShouldBe(10);
        snapshot.Any(static pair => pair.Value is null).ShouldBeFalse("the post-#151 filter forbids null entries");
        snapshot.ShouldContainKeyAndValue("Translator:OrchestratorBaseUrl", expected["COMUKI_ORCH_HTTP"]);
        snapshot.ShouldContainKeyAndValue("Translator:OrchestratorGrpcUrl", expected["COMUKI_ORCH_GRPC"]);
        snapshot.ShouldContainKeyAndValue("Translator:WorkerToken", expected["COMUKI_WORKER_TOKEN"]);
        snapshot.ShouldContainKeyAndValue("Translator:ProfileKey", expected["COMUKI_PROFILE_KEY"]);
        snapshot.ShouldContainKeyAndValue("Translator:ProfilesRef", expected["COMUKI_PROFILES_REF"]);
        snapshot.ShouldContainKeyAndValue("Translator:WorkerImage", expected["COMUKI_WORKER_IMAGE"]);
        snapshot.ShouldContainKeyAndValue("Translator:ProfilesPath", expected["COMUKI_PROFILES_PATH"]);
        snapshot.ShouldContainKeyAndValue("Translator:ProfilesGitUrl", expected["COMUKI_PROFILES_GIT_URL"]);
        snapshot.ShouldContainKeyAndValue("Translator:PiExecutable", expected["COMUKI_PI_EXECUTABLE"]);
        snapshot.ShouldContainKeyAndValue("Translator:WorkingDirectory", expected["COMUKI_WORKING_DIRECTORY"]);
    }
}
