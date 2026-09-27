using Comuki.Modules.Repositories.Domain.Repositories;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Repositories.Unit;

/// <summary>
/// (host, url) identity normalization — the dedup key for
/// <see cref="Repository"/> rows. Casing/whitespace variants resolve to the
/// same identity so the spec's "registering the same (host, url) pair twice
/// SHALL resolve to the existing Repository row" contract holds regardless
/// of how the caller spells the input.
/// </summary>
public sealed class RepositoryIdentityShould
{
    [Fact(DisplayName = "Given trimmed lowercase values, when Of is called, then the identity is returned verbatim")]
    public void OfPreservesAlreadyNormalizedValues()
    {
        var identity = RepositoryIdentity.Of("github.com", "https://github.com/acme/app.git");

        identity.Host.ShouldBe("github.com");
        identity.Url.ShouldBe("https://github.com/acme/app.git");
    }

    [Fact(DisplayName = "Given mixed-case padded values, when Of is called, then both parts are trimmed and lower-cased")]
    public void OfNormalizesMixedCaseAndPadding()
    {
        var identity = RepositoryIdentity.Of("  GitHub.COM ", "  Https://GitHub.com/Acme/App.GIT ");

        identity.Host.ShouldBe("github.com");
        identity.Url.ShouldBe("https://github.com/acme/app.git");
    }

    [Fact(DisplayName = "Given two variants with different casing and whitespace, when Of is called, then both produce the same identity")]
    public void VariantsProduceEqualIdentities()
    {
        var first = RepositoryIdentity.Of("GitHub.COM", "Https://GitHub.com/Acme/App.git");
        var second = RepositoryIdentity.Of("  github.com", " https://github.com/acme/app.git ");

        first.ShouldBe(second);
    }
}
