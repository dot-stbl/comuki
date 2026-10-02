using Comuki.Modules.Projects.Domain.Projects;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Domain mutation surface of <see cref="Project"/> — Create normalization,
/// PATCH Update (null leaves stored), and Archive idempotency.
/// </summary>
public sealed class ProjectDomainShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given messy name and slug, when Create is called, then name is trimmed and slug lower-cased")]
    public void NormalizeOnCreate()
    {
        var project = Project.Create("  Acme Portal  ", "Acme-Portal", "desc", "git://x", "main", now);

        project.Name.ShouldBe("Acme Portal");
        project.Slug.ShouldBe("acme-portal");
        project.Description.ShouldBe("desc");
        project.ProfilesGitUrl.ShouldBe("git://x");
        project.ProfilesGitRef.ShouldBe("main");
        project.Archived.ShouldBeFalse();
        project.ArchivedAt.ShouldBeNull();
        project.CreatedAt.ShouldBe(now);
        project.UpdatedAt.ShouldBe(now);
        project.Id.Value.Version.ShouldBe(7);
    }

    [Fact(DisplayName = "Given a project, when Update supplies only name, then other fields stay and UpdatedAt moves")]
    public void PatchOnlyProvidedFields()
    {
        var project = Project.Create("Old", "old", "keep-me", "git://old", "v1", now);
        var later = now.AddHours(2);

        project.Update(" New Name ", null, null, null, later);

        project.Name.ShouldBe("New Name");
        project.Description.ShouldBe("keep-me");
        project.ProfilesGitUrl.ShouldBe("git://old");
        project.ProfilesGitRef.ShouldBe("v1");
        project.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given a project, when Update supplies every field, then all mutate")]
    public void PatchAllFields()
    {
        var project = Project.Create("Old", "old", "a", "git://a", "a", now);
        var later = now.AddMinutes(5);

        project.Update("Renamed", "b", "git://b", "b", later);

        project.Name.ShouldBe("Renamed");
        project.Description.ShouldBe("b");
        project.ProfilesGitUrl.ShouldBe("git://b");
        project.ProfilesGitRef.ShouldBe("b");
        project.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given an active project, when Archive is called, then flags and timestamps are set")]
    public void ArchiveOnce()
    {
        var project = Project.Create("P", "p", null, null, null, now);
        var later = now.AddDays(1);

        project.Archive(later);

        project.Archived.ShouldBeTrue();
        project.ArchivedAt.ShouldBe(later);
        project.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given an archived project, when Archive is called again, then timestamps stay")]
    public void ArchiveIsIdempotent()
    {
        var project = Project.Create("P", "p", null, null, null, now);
        var first = now.AddDays(1);
        project.Archive(first);

        project.Archive(now.AddDays(9));

        project.ArchivedAt.ShouldBe(first);
        project.UpdatedAt.ShouldBe(first);
    }

    [Fact(DisplayName = "Given identity fields, when Create is called, then the icon is verbatim, the colour lower-cased and the tags normalized")]
    public void NormalizeIdentityOnCreate()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now,
            icon: "🛰️", color: "#3C5A86", tags: ["web", "Billing"]);

        project.Icon.ShouldBe("🛰️");
        project.Color.ShouldBe("#3c5a86");
        project.Tags.ShouldBe(["web", "billing"]);
    }

    [Fact(DisplayName = "Given padded duplicate tags, when Create is called, then the stored list is one trimmed lower-case entry")]
    public void CollapsePaddedDuplicateTagsOnCreate()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, tags: [" web ", "web", "", "WEB"]);

        project.Tags.ShouldBe(["web"]);
    }

    [Fact(DisplayName = "Given no identity arguments, when Create is called, then icon and colour are null and tags empty")]
    public void CreateWithoutIdentity()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now);

        project.Icon.ShouldBeNull();
        project.Color.ShouldBeNull();
        project.Tags.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a stored identity, when Update patches only the colour, then icon and tags stay")]
    public void PatchColourOnlyKeepsIconAndTags()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now,
            icon: "🛰️", color: "#112233", tags: ["web"]);
        var later = now.AddHours(1);

        project.Update(null, null, null, null, later, icon: null, color: "#AABBCC", tags: null);

        project.Icon.ShouldBe("🛰️");
        project.Color.ShouldBe("#aabbcc");
        project.Tags.ShouldBe(["web"]);
        project.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given stored tags, when Update sends an empty list, then tags are cleared")]
    public void EmptyTagsListClearsTags()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, tags: ["web", "billing"]);

        project.Update(null, null, null, null, now, tags: []);

        project.Tags.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given stored tags, when Update omits the tags argument, then tags are unchanged")]
    public void AbsentTagsKeepStoredTags()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, tags: ["web", "billing"]);

        project.Update("Renamed", null, null, null, now);

        project.Tags.ShouldBe(["web", "billing"]);
    }

    [Fact(DisplayName = "Given a project created with an env class, when read, then the class is stored")]
    public void CreateStoresEnvClass()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, envClass: "net10-sdk-bun");

        project.EnvClass.ShouldBe("net10-sdk-bun");
    }

    [Fact(DisplayName = "Given a project created without an env class, when read, then envClass is null")]
    public void CreateWithoutEnvClassLeavesItNull()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now);

        project.EnvClass.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a stored env class, when Update supplies a new class, then it is set")]
    public void PatchEnvClassReplacesStoredValue()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, envClass: "net10-sdk");

        project.Update(null, null, null, null, now.AddMinutes(1), envClass: "net10-sdk-bun");

        project.EnvClass.ShouldBe("net10-sdk-bun");
    }

    [Fact(DisplayName = "Given a stored env class, when Update supplies null, then the stored class is kept")]
    public void PatchNullKeepsStoredEnvClass()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, envClass: "net10-sdk-bun");

        project.Update(null, null, null, null, now.AddMinutes(1), envClass: null);

        project.EnvClass.ShouldBe("net10-sdk-bun");
    }

    [Fact(DisplayName = "Given a stored env class, when Update supplies an empty string, then the class is cleared")]
    public void PatchEmptyStringClearsEnvClass()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, envClass: "net10-sdk-bun");

        project.Update(null, null, null, null, now.AddMinutes(1), envClass: string.Empty);

        project.EnvClass.ShouldBeNull();
    }

    [Fact(DisplayName = "Given an env class with surrounding whitespace, when Create, then it is trimmed")]
    public void CreateTrimsEnvClass()
    {
        var project = Project.Create("Acme", "acme", null, null, null, now, envClass: "  net10-sdk-bun  ");

        project.EnvClass.ShouldBe("net10-sdk-bun");
    }
}
