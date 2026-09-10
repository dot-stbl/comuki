using Comuki.Shared.Kernel.Secrets;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// SecretRefParser tests — the operator-facing reference string splits
/// into (scheme, path, key). Bare names default to the env scheme; the
/// vault / consul branches are parsed but no provider is registered for
/// them in slice 1 — the parsed ref is well-formed, the runtime failure
/// happens at resolve time when no provider answers.
/// </summary>
public sealed class SecretRefParserShould
{
    [Fact(DisplayName = "Given a bare env-var name, when Parse runs, then scheme is env and path is the full name")]
    public void BareNameRoutesToEnv()
    {
        var parsed = SecretRefParser.Parse("GH_TOKEN");

        parsed.Scheme.ShouldBe("env");
        parsed.Path.ShouldBe("GH_TOKEN");
        parsed.Key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given env:NAME, when Parse runs, then scheme is env and path is the trailing segment")]
    public void ExplicitEnvScheme()
    {
        var parsed = SecretRefParser.Parse("env:GH_TOKEN");

        parsed.Scheme.ShouldBe("env");
        parsed.Path.ShouldBe("GH_TOKEN");
        parsed.Key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given file:/abs/path, when Parse runs, then scheme is file and path is the absolute path")]
    public void FileScheme()
    {
        var parsed = SecretRefParser.Parse("file:/etc/comuki/db-pass");

        parsed.Scheme.ShouldBe("file");
        parsed.Path.ShouldBe("/etc/comuki/db-pass");
        parsed.Key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given vault:path#key, when Parse runs, then scheme is vault, path is before '#', and key is after")]
    public void VaultSchemeSplitsKey()
    {
        var parsed = SecretRefParser.Parse("vault:secret/prod/db#password");

        parsed.Scheme.ShouldBe("vault");
        parsed.Path.ShouldBe("secret/prod/db");
        parsed.Key.ShouldBe("password");
    }

    [Fact(DisplayName = "Given consul:key, when Parse runs, then scheme is consul and key is null (consul paths are single-segment)")]
    public void ConsulSchemeWithoutKey()
    {
        var parsed = SecretRefParser.Parse("consul:comuki/prod/db-pass");

        parsed.Scheme.ShouldBe("consul");
        parsed.Path.ShouldBe("comuki/prod/db-pass");
        parsed.Key.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a string with an unknown scheme, when Parse runs, then SecretRefFormatException is thrown")]
    public void UnknownSchemeThrows()
    {
        Should.Throw<SecretRefFormatException>(
            static () => SecretRefParser.Parse("vault2:path"));
    }

    [Fact(DisplayName = "Given a string that starts with a colon (empty scheme), when Parse runs, then it is treated as a bare name (env)")]
    public void LeadingColonFallsBackToBareEnv()
    {
        // An empty scheme (":VAR") is malformed wire format; the parser
        // treats it as a bare name to keep existing rows working.
        var parsed = SecretRefParser.Parse(":GH_TOKEN");

        parsed.Scheme.ShouldBe("env");
        parsed.Path.ShouldBe(":GH_TOKEN");
    }
}
