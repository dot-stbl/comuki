using Comuki.Shared.Kernel.Secrets;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Kernel.Tests;

/// <summary>
/// CompositeSecretResolver tests — dispatches a parsed reference to the
/// matching ISecretProvider by scheme, surfaces unset / unknown refs as
/// typed exceptions, and short-circuits null / whitespace input to null.
/// </summary>
public sealed class CompositeSecretResolverShould
{
    [Fact(DisplayName = "Given a null reference, when ResolveAsync runs, then null is returned (rotation paths can skip the resolver)")]
    public async Task NullReferenceReturnsNullAsync()
    {
        var resolver = new CompositeSecretResolver([]);

        var resolved = await resolver.ResolveAsync(null, TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a whitespace reference, when ResolveAsync runs, then null is returned")]
    public async Task WhitespaceReferenceReturnsNullAsync()
    {
        var resolver = new CompositeSecretResolver([]);

        var resolved = await resolver.ResolveAsync("   ", TestContext.Current.CancellationToken);

        resolved.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a bare env-var name, when ResolveAsync runs, then the env provider answers")]
    public async Task BareNameRoutesToEnvProviderAsync()
    {
        var env = Substitute.For<ISecretProvider>();
        env.Scheme.Returns("env");
        env.ResolveAsync(Arg.Is<SecretRef>(static r => r.Scheme == "env" && r.Path == "GH_TOKEN"), Arg.Any<CancellationToken>())
            .Returns("gh-value");
        var resolver = new CompositeSecretResolver([env]);

        var resolved = await resolver.ResolveAsync("GH_TOKEN", TestContext.Current.CancellationToken);

        resolved.ShouldBe("gh-value");
    }

    [Fact(DisplayName = "Given env:NAME, when ResolveAsync runs, then the env provider is asked with the parsed env ref")]
    public async Task PrefixedEnvRoutesToEnvProviderAsync()
    {
        var env = Substitute.For<ISecretProvider>();
        env.Scheme.Returns("env");
        env.ResolveAsync(Arg.Is<SecretRef>(static r => r.Scheme == "env" && r.Path == "GH_TOKEN"), Arg.Any<CancellationToken>())
            .Returns("gh-value");
        var resolver = new CompositeSecretResolver([env]);

        var resolved = await resolver.ResolveAsync("env:GH_TOKEN", TestContext.Current.CancellationToken);

        resolved.ShouldBe("gh-value");
    }

    [Fact(DisplayName = "Given file:/path, when ResolveAsync runs, then the file provider is asked with the parsed file ref")]
    public async Task FilePrefixRoutesToFileProviderAsync()
    {
        var env = Substitute.For<ISecretProvider>();
        env.Scheme.Returns("env");
        var file = Substitute.For<ISecretProvider>();
        file.Scheme.Returns("file");
        file.ResolveAsync(Arg.Is<SecretRef>(static r => r.Scheme == "file" && r.Path == "/etc/comuki/gh"), Arg.Any<CancellationToken>())
            .Returns("file-value");
        var resolver = new CompositeSecretResolver([env, file]);

        var resolved = await resolver.ResolveAsync("file:/etc/comuki/gh", TestContext.Current.CancellationToken);

        resolved.ShouldBe("file-value");
    }

    [Fact(DisplayName = "Given a ref whose provider returns null, when ResolveAsync runs, then SecretRefUnsetException is thrown")]
    public async Task UnsetProviderResponseThrowsUnsetExceptionAsync()
    {
        var env = Substitute.For<ISecretProvider>();
        env.Scheme.Returns("env");
        env.ResolveAsync(Arg.Any<SecretRef>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        var resolver = new CompositeSecretResolver([env]);

        var exception = await Should.ThrowAsync<SecretRefUnsetException>(
            () => resolver.ResolveAsync("env:GH_TOKEN", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("env:GH_TOKEN");
    }

    [Fact(DisplayName = "Given a ref whose provider returns empty string, when ResolveAsync runs, then SecretRefUnsetException is thrown")]
    public async Task EmptyProviderResponseThrowsUnsetExceptionAsync()
    {
        var env = Substitute.For<ISecretProvider>();
        env.Scheme.Returns("env");
        env.ResolveAsync(Arg.Any<SecretRef>(), Arg.Any<CancellationToken>())
            .Returns(string.Empty);
        var resolver = new CompositeSecretResolver([env]);

        await Should.ThrowAsync<SecretRefUnsetException>(
            () => resolver.ResolveAsync("env:GH_TOKEN", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given a ref whose scheme has no registered provider, when ResolveAsync runs, then SecretRefFormatException is thrown")]
    public async Task UnknownSchemeThrowsFormatExceptionAsync()
    {
        var resolver = new CompositeSecretResolver([]);

        await Should.ThrowAsync<SecretRefFormatException>(
            () => resolver.ResolveAsync("vault:secret#password", TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an unknown scheme at the parser level (e.g. 'foo:VAR'), when ResolveAsync runs, then SecretRefFormatException is thrown before any provider is consulted")]
    public async Task MalformedSchemeThrowsFormatExceptionAsync()
    {
        var resolver = new CompositeSecretResolver([]);

        await Should.ThrowAsync<SecretRefFormatException>(
            () => resolver.ResolveAsync("foo:VAR", TestContext.Current.CancellationToken));
    }
}
