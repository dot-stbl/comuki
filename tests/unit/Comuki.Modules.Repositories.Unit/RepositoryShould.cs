using Comuki.Modules.Repositories.Domain.Repositories;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Repositories.Unit;

/// <summary>
/// Domain mutation surface of <see cref="Repository"/>: Create normalizes
/// the (host, url) pair and defaults a blank branch to <c>main</c>;
/// Update is PATCH semantics (null leaves the stored value). The dedup key
/// (<see cref="Repository.MatchesIdentity"/>) resolves casing/whitespace
/// variants to the same Repository row — the spec's "registering the same
/// (host, url) pair twice SHALL resolve to the existing Repository row"
/// contract lives here.
/// </summary>
public sealed class RepositoryShould
{
    private readonly DateTimeOffset now = new(2026, 9, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given messy url/host and a branch, when Create is called, then url/host are normalized and the branch is trimmed")]
    public void NormalizeOnCreate()
    {
        var repository = Repository.Create(
            "  Https://GitHub.com/Acme/App.GIT ",
            "  GitHub.COM ",
            "  Main ",
            now);

        repository.Url.ShouldBe("https://github.com/acme/app.git");
        repository.Host.ShouldBe("github.com");
        repository.DefaultBranch.ShouldBe("Main");
        repository.CreatedAt.ShouldBe(now);
        repository.UpdatedAt.ShouldBe(now);
        repository.Id.Value.Version.ShouldBe(7);
    }

    [Fact(DisplayName = "Given a blank default branch, when Create is called, then the branch defaults to main")]
    public void DefaultBranchDefaultsToMainWhenBlank()
    {
        var repository = Repository.Create("https://x.com/y.git", "x.com", "   ", now);

        repository.DefaultBranch.ShouldBe("main");
    }

    [Fact(DisplayName = "Given a stored repository, when Update supplies only a branch, then url/host stay and the timestamp moves")]
    public void PatchOnlyProvidedFields()
    {
        var repository = Repository.Create("https://old.example/o/r.git", "old.example", "v1", now);
        var later = now.AddHours(2);

        repository.Update(url: null, host: null, defaultBranch: "  v2 ", later);

        repository.Url.ShouldBe("https://old.example/o/r.git");
        repository.Host.ShouldBe("old.example");
        repository.DefaultBranch.ShouldBe("v2");
        repository.UpdatedAt.ShouldBe(later);
        repository.CreatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a stored repository, when Update supplies a null branch, then the branch stays")]
    public void NullBranchPreservesStoredValue()
    {
        var repository = Repository.Create("https://x.example/o/r.git", "x.example", "main", now);

        repository.Update(url: null, host: null, defaultBranch: null, now.AddMinutes(5));

        repository.DefaultBranch.ShouldBe("main");
    }

    [Fact(DisplayName = "Given a blank branch through Update, when the patch lands, then the branch defaults to main again")]
    public void BlankBranchThroughUpdateDefaultsToMain()
    {
        var repository = Repository.Create("https://x.example/o/r.git", "x.example", "develop", now);

        repository.Update(url: null, host: null, defaultBranch: "   ", now.AddMinutes(5));

        repository.DefaultBranch.ShouldBe("main");
    }

    [Fact(DisplayName = "Given a stored repository, when MatchesIdentity is called with the same key, then it returns true")]
    public void MatchesIdentityTrueForExactKey()
    {
        var repository = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);

        repository.MatchesIdentity("github.com", "https://github.com/acme/app.git").ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a stored repository, when MatchesIdentity is called with casing/whitespace variants, then it returns true")]
    public void MatchesIdentityTrueForVariants()
    {
        var repository = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);

        repository.MatchesIdentity("GitHub.COM", " Https://GitHub.com/Acme/App.GIT ").ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a stored repository, when MatchesIdentity is called with a different host, then it returns false")]
    public void MatchesIdentityFalseForDifferentHost()
    {
        var repository = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);

        repository.MatchesIdentity("gitlab.com", "https://github.com/acme/app.git").ShouldBeFalse();
    }

    [Fact(DisplayName = "Given two Create calls with variant spellings, when Identity is compared, then the two identities are equal")]
    public void TwoCreatesWithVariantsProduceEqualIdentities()
    {
        var first = Repository.Create("https://github.com/acme/app.git", "github.com", "main", now);
        var second = Repository.Create("  Https://GitHub.com/Acme/App.GIT ", "  GitHub.COM ", "main", now);

        first.Identity.ShouldBe(second.Identity);
    }
}
