using Comuki.Shared.Editions.Options;
using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Editions.Unit.Options;

/// <summary>
/// Tests for <see cref="LicenseOptionsValidator"/> — the options-level
/// gate that fails boot loudly when <see cref="LicenseOptions.Path"/>
/// is configured but unresolvable, or when the numeric ranges are
/// violated. Uses <c>NSubstitute</c> for <see cref="ISecretResolver"/>
/// so every branch (returns / throws unset / throws format) can be
/// driven without filesystem I/O.
/// </summary>
public sealed class LicenseOptionsValidatorShould
{
    private static LicenseOptions ValidOptions(string? path = null) => new()
    {
        Path = path,
        GracePeriod = TimeSpan.FromDays(7),
        ReloadDelay = TimeSpan.FromSeconds(5),
    };

    private static ValidateOptionsResult Validate(LicenseOptions options, ISecretResolver resolver)
    {
        return new LicenseOptionsValidator(resolver).Validate(name: null, options);
    }

    [Fact(DisplayName = "Given no Path configured, when Validate runs, then succeeds and the resolver is never called")]
    public void NullPathSkipsResolver()
    {
        var resolver = Substitute.For<ISecretResolver>();

        var result = Validate(ValidOptions(path: null), resolver);

        result.Succeeded.ShouldBeTrue();
        resolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default!);
    }

    [Fact(DisplayName = "Given a Path that the resolver returns a value for, when Validate runs, then succeeds")]
    public void ResolvablePathSucceeds()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync("env:LICENSE", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("opaque-token-bytes"));

        var result = Validate(ValidOptions(path: "env:LICENSE"), resolver);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a Path that the resolver reports as unset, when Validate runs, then fails and the failure message contains the path")]
    public void UnsetPathFailsWithPathInMessage()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new SecretRefUnsetException("env:MISSING"));

        var result = Validate(ValidOptions(path: "env:MISSING"), resolver);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("env:MISSING"));
    }

    [Fact(DisplayName = "Given a Path whose scheme the resolver rejects as malformed, when Validate runs, then fails and the failure message contains the path")]
    public void MalformedPathFailsWithPathInMessage()
    {
        var resolver = Substitute.For<ISecretResolver>();
        resolver.ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new SecretRefFormatException("scheme bogus is unsupported"));

        var result = Validate(ValidOptions(path: "bogus:ref"), resolver);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("bogus:ref"));
    }

    [Fact(DisplayName = "Given a negative GracePeriod, when Validate runs, then fails")]
    public void NegativeGracePeriodFails()
    {
        var resolver = Substitute.For<ISecretResolver>();

        var result = Validate(new LicenseOptions
        {
            GracePeriod = TimeSpan.FromDays(-1),
            ReloadDelay = TimeSpan.FromSeconds(5),
        }, resolver);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("GracePeriod"));
    }

    [Fact(DisplayName = "Given a zero ReloadDelay, when Validate runs, then fails")]
    public void ZeroReloadDelayFails()
    {
        var resolver = Substitute.For<ISecretResolver>();

        var result = Validate(new LicenseOptions
        {
            GracePeriod = TimeSpan.FromDays(7),
            ReloadDelay = TimeSpan.Zero,
        }, resolver);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("ReloadDelay"));
    }

    [Fact(DisplayName = "Given a negative ReloadDelay, when Validate runs, then fails")]
    public void NegativeReloadDelayFails()
    {
        var resolver = Substitute.For<ISecretResolver>();

        var result = Validate(new LicenseOptions
        {
            GracePeriod = TimeSpan.FromDays(7),
            ReloadDelay = TimeSpan.FromSeconds(-5),
        }, resolver);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("ReloadDelay"));
    }
}
