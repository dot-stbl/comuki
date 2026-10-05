using Comuki.Host.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.ProfileCatalog;

/// <summary>
/// Unit tests for <see cref="SkillCatalog"/> against temp-dir control-plane
/// trees: ordering, key lookup, malformed-document tolerance, and the
/// split between <c>ValidateAgainstPaths</c> (plain string) and
/// <c>ValidateAgainstRefs</c> (SourceRef-shaped objects).
/// </summary>
public sealed class SkillCatalogShould
{
    private static SkillCatalog CreateCatalog(string root)
    {
        return new SkillCatalog(
            Options.Create(new ControlPlaneCatalogOptions { Root = root }),
            NullLogger<SkillCatalog>.Instance);
    }

    [Fact(DisplayName = "Given a skills folder, when ListAsync, then skills are ordered by key with full metadata mapped")]
    public async Task ListSkillsOrderedByKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteSkill(
            "citation-cleanup",
            """
            ---
            name: citation-cleanup
            description: Cleans up inline citations
            trigger_when: drafting a long-form document that needs citation cleanup
            validate_against:
              - "../../rules/citation-format.md"
              - { kind: knowledge, id: "doc/citation-style@v3" }
            version: 1.2.0
            ---

            Strip duplicates, normalise.
            """);
        tree.WriteSkill(
            "git-workflow",
            """
            ---
            name: git-workflow
            description: Safe branch and commit flow
            ---

            Branch first.
            """);

        var skills = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        skills.Select(static skill => skill.Key).ShouldBe(["citation-cleanup", "git-workflow"]);

        var citation = skills[0];
        citation.Name.ShouldBe("citation-cleanup");
        citation.Description.ShouldBe("Cleans up inline citations");
        citation.Version.ShouldBe("1.2.0");
        citation.TriggerWhen.ShouldBe([
            "drafting a long-form document that needs citation cleanup",
        ]);
        citation.ValidateAgainstPaths.ShouldBe(["../../rules/citation-format.md"]);
        citation.ValidateAgainstRefs.ShouldHaveSingleItem();
        citation.ValidateAgainstRefs[0].Kind.ShouldBe("knowledge");
        citation.ValidateAgainstRefs[0].Id.ShouldBe("doc/citation-style@v3");

        var git = skills[1];
        git.Version.ShouldBe("0.1.0");
        git.TriggerWhen.ShouldBeEmpty();
        git.ValidateAgainstPaths.ShouldBeEmpty();
        git.ValidateAgainstRefs.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a malformed skill, when ListAsync, then it is skipped and the rest of the catalog survives")]
    public async Task SkipMalformedDocumentsAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteSkill("good", "---\nname: good\ndescription: Valid.\n---\n\nBody.");
        tree.WriteSkill("broken", "# no frontmatter at all");
        tree.WriteSkill("nameless", "---\ndescription: Name is missing.\n---\n\nBody.");

        var skills = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        skills.ShouldHaveSingleItem().Key.ShouldBe("good");
    }

    [Fact(DisplayName = "Given a skill directory without SKILL.md, when ListAsync, then the directory is skipped")]
    public async Task SkipSkillDirectoryWithoutFileAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteSkill("good", "---\nname: good\ndescription: Valid.\n---\n\nBody.");
        Directory.CreateDirectory(Path.Combine(tree.SkillsRoot, "empty-dir"));

        var skills = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        skills.ShouldHaveSingleItem().Key.ShouldBe("good");
    }

    [Fact(DisplayName = "Given an existing skill key, when GetAsync, then the skill is returned")]
    public async Task GetSkillByKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteSkill(
            "citation-cleanup",
            """
            ---
            name: citation-cleanup
            description: Cleans up inline citations
            version: 1.2.0
            ---

            Body.
            """);

        var skill = await CreateCatalog(tree.Root).GetAsync("citation-cleanup", TestContext.Current.CancellationToken);

        skill.ShouldNotBeNull();
        skill!.Key.ShouldBe("citation-cleanup");
        skill.Version.ShouldBe("1.2.0");
    }

    [Fact(DisplayName = "Given an unknown skill key, when GetAsync, then returns null")]
    public async Task ReturnNullForUnknownKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteSkill("git-workflow", "---\nname: git-workflow\ndescription: Workflow.\n---\n\nBody.");

        var skill = await CreateCatalog(tree.Root).GetAsync("does-not-exist", TestContext.Current.CancellationToken);

        skill.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a root without the catalog folder, when listing, then the catalog is empty and nothing throws")]
    public async Task ReturnEmptyWhenFolderMissingAsync()
    {
        using var tree = new TempControlPlane();
        Directory.CreateDirectory(tree.Root);

        var skills = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        skills.ShouldBeEmpty();
    }
}
