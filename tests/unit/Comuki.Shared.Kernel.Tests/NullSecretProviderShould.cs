using Comuki.Shared.Kernel.Secrets;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// NullSecretProvider tests — always returns null. Used as a DI default
/// when no provider is configured for a scheme and as a test fake.
/// </summary>
public sealed class NullSecretProviderShould
{
    [Fact(DisplayName = "Given any reference, when ResolveAsync runs, then null is always returned (the resolver turns it into SecretRefUnsetException)")]
    public async Task ResolveAsyncAlwaysReturnsNullAsync()
    {
        var provider = new NullSecretProvider();

        var resolved = await provider.ResolveAsync(new SecretRef("env", "ANY", null), TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a provider, its Scheme is the lowercase 'null' string (intentionally distinct from 'env' / 'file' / 'vault' / 'consul')")]
    public void SchemeIsNull()
    {
        var provider = new NullSecretProvider();
        provider.Scheme.ShouldBe("null");
    }
}
