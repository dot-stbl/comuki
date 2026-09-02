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
}
