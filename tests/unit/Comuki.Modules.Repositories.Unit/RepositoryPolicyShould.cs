using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Repositories.Unit;

/// <summary>
/// Normalization contract for <see cref="RepositoryPolicy"/>'s three
/// rule lists (protected branches, required checks, approvers). Each list
/// is trimmed, lower-cased, de-duplicated, sorted ordinal and stripped of
/// blanks — so two snapshots of the same source land as identical arrays
/// in the database. <see cref="RepositoryPolicy.Replace"/> carries the
/// same normalization through to refreshes from the host API.
/// </summary>
public sealed class RepositoryPolicyShould
{
    private readonly DateTimeOffset now = new(2026, 9, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given messy input across all three lists, when Create is called, then every list is normalized, deduplicated and sorted")]
    public void NormalizeAllListsOnCreate()
    {
        var policy = RepositoryPolicy.Create(
            RepositoryId.New(),
            protectedBranches: ["  Main ", "develop", "main", "  ", "RELEASE/*"],
            requiredChecks: ["Build", "  build ", "Lint", " "],
            approvers: ["alice", "Alice", "bob"],
            now);

        policy.ProtectedBranches.ShouldBe(["develop", "main", "release/*"]);
        policy.RequiredChecks.ShouldBe(["build", "lint"]);
        policy.Approvers.ShouldBe(["alice", "bob"]);
        policy.CreatedAt.ShouldBe(now);
        policy.UpdatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given only whitespace and duplicates, when Create is called, then the lists end up empty")]
    public void EmptyListCollapsesToEmptyArray()
    {
        var policy = RepositoryPolicy.Create(
            RepositoryId.New(),
            protectedBranches: ["  ", "", "main", "MAIN"],
            requiredChecks: [],
            approvers: ["   "],
            now);

        policy.ProtectedBranches.ShouldBe(["main"]);
        policy.RequiredChecks.ShouldBeEmpty();
        policy.Approvers.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a stored policy, when Replace is called with new lists, then the lists are re-normalized and UpdatedAt moves")]
    public void ReplaceNormalizesAndUpdatesTimestamp()
    {
        var policy = RepositoryPolicy.Create(
            RepositoryId.New(),
            protectedBranches: ["main"],
            requiredChecks: ["lint"],
            approvers: ["alice"],
            now);
        var later = now.AddHours(3);

        policy.Replace(
            protectedBranches: ["RELEASE/*", "release/*", "develop", " "],
            requiredChecks: ["Build"],
            approvers: ["bob"],
            later);

        policy.ProtectedBranches.ShouldBe(["develop", "release/*"]);
        policy.RequiredChecks.ShouldBe(["build"]);
        policy.Approvers.ShouldBe(["bob"]);
        policy.UpdatedAt.ShouldBe(later);
        policy.CreatedAt.ShouldBe(now);
    }
}
