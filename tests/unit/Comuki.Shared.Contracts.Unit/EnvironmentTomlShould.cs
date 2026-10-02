using Comuki.Shared.Contracts.Environments;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Contracts.Unit;

/// <summary>
/// Parser contract for <c>.comuki/environment.toml</c>
/// (add-worker-environments 4.1, spec scenario "Valid Comuki dogfood file is
/// accepted" and the closed-schema rejections). The Host reads the file on
/// attach; the Translator runs the restore opcodes after clone — both
/// trust the same projection this parser emits.
/// </summary>
public sealed class EnvironmentTomlShould
{
    private const string DogfoodToml = """
        schema = 1
        class = "net10-sdk-bun"
        runtime = "linux"

        [restore]
        dotnet = "comuki.slnx"
        bun = ["dashboard", "agents"]
        """;

    [Fact(DisplayName = "Given the Comuki dogfood file, when TryParse runs, then class, runtime, restore, and schema all resolve")]
    public void DogfoodFileIsAccepted()
    {
        var accepted = EnvironmentToml.TryParse(DogfoodToml, out var file, out var errors);

        accepted.ShouldBeTrue();
        errors.ShouldBeEmpty();
        file.ShouldNotBeNull();
        file!.Class.ShouldBe("net10-sdk-bun");
        file.Runtime.ShouldBe("linux");
        file.Restore.Count.ShouldBe(2);
        file.Restore["dotnet"].ShouldBe(["comuki.slnx"]);
        file.Restore["bun"].ShouldBe(["dashboard", "agents"]);
        file.Mounts.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a file with postCreate key, when TryParse runs, then parse fails on the unknown key")]
    public void PostCreateKeyIsRejected()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "linux"
            postCreate = "echo hi"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("postCreate", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a file with run key, when TryParse runs, then parse fails on the unknown key")]
    public void RunKeyIsRejected()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "linux"
            run = "echo hi"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("'run'", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given schema = 2, when TryParse runs, then parse fails with a schema mismatch error")]
    public void SchemaTwoIsRejected()
    {
        var toml = """
            schema = 2
            class = "net10-sdk-bun"
            runtime = "linux"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("schema", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a file missing the class key, when TryParse runs, then parse fails with a missing-class error")]
    public void MissingClassIsRejected()
    {
        var toml = """
            schema = 1
            runtime = "linux"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("class", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given runtime = 'windows-server', when TryParse runs, then parse fails because only linux and windows are allowed")]
    public void BadRuntimeIsRejected()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "windows-server"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Given restore.bun = 42 (integer), when TryParse runs, then parse fails because restore values must be string or array")]
    public void RestoreIntegerValueIsRejected()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "linux"

            [restore]
            bun = 42
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("restore.bun", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given JSON content, when TryParse runs, then parse fails because the toml surface refuses non-TOML wire formats")]
    public void JsonContentIsRejected()
    {
        var json = /*lang=json,strict*/ """
            {
              "schema": 1,
              "class": "net10-sdk-bun",
              "runtime": "linux"
            }
            """;

        var accepted = EnvironmentToml.TryParse(json, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact(DisplayName = "Given a scalar restore value, when TryParse runs, then the value normalizes to a single-item list")]
    public void ScalarRestoreNormalizesToSingleItemArray()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "linux"

            [restore]
            bun = "dashboard"
            """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeTrue();
        errors.ShouldBeEmpty();
        file!.Restore["bun"].ShouldBe(["dashboard"]);
    }

    [Fact(DisplayName = "Given a file with an unknown top-level scalar (features), when TryParse runs, then parse fails on the unknown key")]
    public void UnknownTopLevelScalarIsRejected()
    {
        var toml = """
            schema = 1
            class = "net10-sdk-bun"
            runtime = "linux"
        features = ["ghcr.io/devcontainers/features/dotnet:2"]
        """;

        var accepted = EnvironmentToml.TryParse(toml, out var file, out var errors);

        accepted.ShouldBeFalse();
        file.ShouldBeNull();
        errors.ShouldContain(static e => e.Contains("features", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a parsed file with restore bun, when ValidateRestoreOpcodes runs against allowed={dotnet}, then bun is rejected")]
    public void ValidateRestoreOpcodesRejectsDisallowedKey()
    {
        EnvironmentToml.TryParse(DogfoodToml, out var file, out _).ShouldBeTrue();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "dotnet" };

        var ok = EnvironmentToml.ValidateRestoreOpcodes(file!, allowed, out var errors);

        ok.ShouldBeFalse();
        errors.ShouldContain(static e => e.Contains("bun", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a parsed file with restore dotnet + bun, when ValidateRestoreOpcodes runs against allowed={dotnet,bun}, then every key passes")]
    public void ValidateRestoreOpcodesAcceptsAdvertisedSet()
    {
        EnvironmentToml.TryParse(DogfoodToml, out var file, out _).ShouldBeTrue();
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "dotnet", "bun" };

        var ok = EnvironmentToml.ValidateRestoreOpcodes(file!, allowed, out var errors);

        ok.ShouldBeTrue();
        errors.ShouldBeEmpty();
    }
}
