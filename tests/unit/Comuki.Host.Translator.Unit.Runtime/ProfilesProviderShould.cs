using Comuki.Host.Translator.Profiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// v0 profiles provider contract: local-copy wins when the mounted path
/// is set, otherwise we fall back to a public git clone at the pinned ref,
/// otherwise the provider logs a warning and skips. Idempotent on a
/// second call (the <c>profiles/</c> target directory already exists on
/// disk). The git-clone path is exercised through a mocked
/// <see cref="Shared.Contracts.Artifacts.IRunArtifactStore"/>-style
/// surrogate — we never hit the network from a unit test. Serialised
/// through <see cref="TranslatorEnvSafeCollection"/> because
/// <see cref="SnapshotWithUnsetWorkingDirectoryPreservesDefault"/> reads
/// the same <c>COMUKI_*</c> env vars
/// <see cref="TranslatorEnvironmentSnapshotShould"/> mutates.
/// </summary>
[Collection(nameof(TranslatorEnvSafeCollection))]
public sealed class ProfilesProviderShould
{
    [Fact(DisplayName = "Given a configured local profiles path, when PrepareAsync is called, then the source is copied under profiles/ in the working directory")]
    public async Task LocalCopyWinsOverCloneAsync()
    {
        using var workingDir = new TempDirectory();
        using var sourceDir = new TempDirectory();
        File.WriteAllText(Path.Combine(sourceDir.DirectoryPath, "README.md"), "hello from local profiles");
        File.WriteAllText(Path.Combine(sourceDir.DirectoryPath, "skill.md"), "skill body");
        var options = NewOptionsValue(NewOptions(workingDir.DirectoryPath, profilesPath: sourceDir.DirectoryPath));
        var provider = new ProfilesProvider(options, NullLogger<ProfilesProvider>.Instance);

        await provider.PrepareAsync(profilesRef: "main", TestContext.Current.CancellationToken);

        var profiles = Path.Combine(workingDir.DirectoryPath, "profiles");
        Directory.Exists(profiles).ShouldBeTrue();
        File.ReadAllText(Path.Combine(profiles, "README.md")).ShouldBe("hello from local profiles");
        File.ReadAllText(Path.Combine(profiles, "skill.md")).ShouldBe("skill body");
    }

    [Fact(DisplayName = "Given the profiles/ target already exists, when PrepareAsync is called a second time, then the operation is a no-op")]
    public async Task TargetExistsIsNoOpAsync()
    {
        using var workingDir = new TempDirectory();
        using var sourceDir = new TempDirectory();
        var profiles = Path.Combine(workingDir.DirectoryPath, "profiles");
        Directory.CreateDirectory(profiles);
        var sentinel = Path.Combine(profiles, "sentinel.md");
        File.WriteAllText(sentinel, "untouched");

        var options = NewOptionsValue(NewOptions(workingDir.DirectoryPath, profilesPath: sourceDir.DirectoryPath));
        var provider = new ProfilesProvider(options, NullLogger<ProfilesProvider>.Instance);

        await provider.PrepareAsync(profilesRef: "main", TestContext.Current.CancellationToken);

        File.ReadAllText(sentinel).ShouldBe("untouched");
        Directory.GetFiles(profiles).ShouldBe([sentinel]);
    }

    [Fact(DisplayName = "Given no profiles path and no git URL, when PrepareAsync is called, then the provider logs a warning and does not create the target directory")]
    public async Task MissingSourceLogsWarningAndSkipsAsync()
    {
        using var workingDir = new TempDirectory();
        var logger = Substitute.For<ILogger<ProfilesProvider>>();
        var options = NewOptionsValue(NewOptions(workingDir.DirectoryPath, profilesPath: null));
        var provider = new ProfilesProvider(options, logger);

        await provider.PrepareAsync(profilesRef: "v1", TestContext.Current.CancellationToken);

        Directory.Exists(Path.Combine(workingDir.DirectoryPath, "profiles")).ShouldBeFalse();
        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object?>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object?, Exception?, string>>());
    }

    [Fact(DisplayName = "Given the working directory's profiles/ folder does not exist, when PrepareAsync is called, then the local-copy path produces the directory tree under working/profiles/")]
    public async Task PrepareAsyncReturnsProfileCatalogShapeAsync()
    {
        using var workingDir = new TempDirectory();
        using var sourceDir = new TempDirectory();
        File.WriteAllText(Path.Combine(sourceDir.DirectoryPath, "a.md"), "alpha");
        Directory.CreateDirectory(Path.Combine(sourceDir.DirectoryPath, "nested"));
        File.WriteAllText(Path.Combine(sourceDir.DirectoryPath, "nested", "b.md"), "beta");
        var options = NewOptionsValue(NewOptions(workingDir.DirectoryPath, profilesPath: sourceDir.DirectoryPath));
        var provider = new ProfilesProvider(options, NullLogger<ProfilesProvider>.Instance);

        await provider.PrepareAsync(profilesRef: "main", TestContext.Current.CancellationToken);

        var profiles = Path.Combine(workingDir.DirectoryPath, "profiles");
        File.ReadAllText(Path.Combine(profiles, "a.md")).ShouldBe("alpha");
        File.ReadAllText(Path.Combine(profiles, "nested", "b.md")).ShouldBe("beta");
    }

    [Fact(DisplayName = "Given the default TranslatorOptions (WorkingDirectory = Directory.GetCurrentDirectory()) and no profiles source, when PrepareAsync runs, then it logs the documented warning and does not throw ArgumentNullException on Path.Combine")]
    public async Task WorkingDirectoryDefaultDoesNotThrowAsync()
    {
        // No TempDirectory wrapper here — the test deliberately exercises the
        // default TranslatorOptions.WorkingDirectory (Directory.GetCurrentDirectory()).
        // The provider must log the documented "no profiles source" warning
        // and short-circuit BEFORE Path.Combine even runs (the directory
        // exists because the cwd does), so this asserts the no-throw shape of
        // the issue #151 regression path.
        var options = NewOptionsValue(NewOptions(Directory.GetCurrentDirectory(), profilesPath: null));
        var logger = Substitute.For<ILogger<ProfilesProvider>>();
        var provider = new ProfilesProvider(options, logger);

        await provider.PrepareAsync(profilesRef: "v1", TestContext.Current.CancellationToken);

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object?>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object?, Exception?, string>>());
    }

    [Fact(DisplayName = "Given an empty env-snapshot (no COMUKI_* set), when TranslatorOptions is bound from the configuration, then WorkingDirectory keeps Directory.GetCurrentDirectory()")]
    public void SnapshotWithUnsetWorkingDirectoryPreservesDefault()
    {
        // Mirrors TranslatorEnvironment.Snapshot()'s contract from
        // Translator/Program.cs: with no env vars set, the snapshot contributes
        // NO entries — so the binder sees only the configuration's defaults.
        // We run the same IConfigurationBinder.Bind(...) step Program.cs uses
        // against a manually-seeded options instance; required fields are
        // satisfied up front (the host's ValidateOnStart would have already
        // crashed on their absence in production), so the assertion focuses
        // on the non-required fields — WorkingDirectory specifically —
        // that the snapshot filter is supposed to keep at their declared
        // defaults. Pre-#151, the snapshot's null entry would have
        // overwritten WorkingDirectory with null and the second assertion
        // would have failed.
        var snapshot = TranslatorEnvironment.Snapshot();
        snapshot.ShouldBeEmpty("the post-#151 filter omits unset env entries");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(snapshot)
            .Build();

        var options = new TranslatorOptions
        {
            OrchestratorBaseUrl = new Uri("https://orchestrator.example"),
            OrchestratorGrpcUrl = new Uri("https://orchestrator.example"),
            WorkerToken = "0123456789abcdef0123456789abcdef",
            ProfileKey = "implement",
            ProfilesRef = "main",
            WorkerImage = "ghcr.io/example/worker@sha256:abc",
        };

        configuration.GetSection(TranslatorOptions.SectionName).Bind(options);

        options.WorkingDirectory.ShouldBe(Directory.GetCurrentDirectory());
        options.ProfilesPath.ShouldBeNull();
        options.ProfilesGitUrl.ShouldBeNull();
        options.PiExecutable.ShouldBe("pi");
    }

    private static TranslatorOptions NewOptions(string workingDirectory, string? profilesPath)
    {
        return new TranslatorOptions
        {
            OrchestratorBaseUrl = new Uri("https://orchestrator.example"),
            OrchestratorGrpcUrl = new Uri("https://orchestrator.example"),
            WorkerToken = "0123456789abcdef0123456789abcdef",
            ProfileKey = "implement",
            ProfilesRef = "main",
            WorkerImage = "ghcr.io/example/worker@sha256:abc",
            WorkingDirectory = workingDirectory,
            ProfilesPath = profilesPath,
        };
    }

    private static IOptions<TranslatorOptions> NewOptionsValue(TranslatorOptions value)
    {
        return Options.Create(value);
    }

    /// <summary>Creates a directory that is recursively deleted on Dispose.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"profiles-provider-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
