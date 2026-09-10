using Comuki.Shared.Kernel.Secrets;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// EnvSecretProvider tests — reads from the process environment under
/// the <c>env</c> scheme. The tests clean their own env vars to avoid
/// cross-test contamination.
/// </summary>
public sealed class EnvSecretProviderShould
{
    private const string EnvName = "COMUKI_TEST_SECRET_ENV_PROVIDER";

    public EnvSecretProviderShould()
    {
        Environment.SetEnvironmentVariable(EnvName, null);
    }

    [Fact(DisplayName = "Given a set env var, when ResolveAsync runs, then the value is returned")]
    public async Task ResolveAsyncReturnsValueAsync()
    {
        Environment.SetEnvironmentVariable(EnvName, "secret-value");
        var provider = new EnvSecretProvider();

        var resolved = await provider.ResolveAsync(new SecretRef("env", EnvName, null), TestContext.Current.CancellationToken);

        resolved.ShouldBe("secret-value");
    }

    [Fact(DisplayName = "Given an unset env var, when ResolveAsync runs, then null is returned (the resolver turns it into SecretRefUnsetException)")]
    public async Task ResolveAsyncReturnsNullWhenUnsetAsync()
    {
        var provider = new EnvSecretProvider();

        var resolved = await provider.ResolveAsync(new SecretRef("env", EnvName, null), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an env var set to empty string, when ResolveAsync runs, then empty string is returned (env reads the literal)")]
    public async Task ResolveAsyncReturnsEmptyWhenSetToEmptyAsync()
    {
        Environment.SetEnvironmentVariable(EnvName, string.Empty);
        var provider = new EnvSecretProvider();

        var resolved = await provider.ResolveAsync(new SecretRef("env", EnvName, null), TestContext.Current.CancellationToken);

        // The provider returns what the OS returns; the COMPOSITE
        // resolver is the one that turns empty into the typed error.
        resolved.ShouldBe(string.Empty);
    }

    [Fact(DisplayName = "Given a null reference name in SecretRef.Path, when ResolveAsync runs, then null is returned (Environment.GetEnvironmentVariable handles gracefully)")]
    public async Task ResolveAsyncReturnsNullForNullNameAsync()
    {
        var provider = new EnvSecretProvider();

        var resolved = await provider.ResolveAsync(new SecretRef("env", string.Empty, null), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a provider, its Scheme is the lowercase 'env' string")]
    public void SchemeIsEnv()
    {
        var provider = new EnvSecretProvider();
        provider.Scheme.ShouldBe("env");
    }
}
