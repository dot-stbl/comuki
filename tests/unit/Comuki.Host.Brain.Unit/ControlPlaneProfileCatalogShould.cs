using Comuki.Host.Brain.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Brain.Unit;

/// <summary>
/// The brain-side profile catalog over the control-plane folder: valid
/// documents parse (frontmatter name/description/allowedTools/model),
/// malformed documents are skipped, a missing folder answers empty.
/// </summary>
public sealed class ControlPlaneProfileCatalogShould
{
    [Fact(DisplayName = "Given a folder of profile documents, when listed, then frontmatter maps to the definition")]
    public async Task ParseProfileFrontmatterAsync()
    {
        using var folder = new TempProfileFolder(
            ("implement", """
                ---
                name: Implementer
                description: writes the code
                allowedTools:
                  - Read
                  - Bash
                model: heavy
                ---
                body is ignored
                """));

        var catalog = new ControlPlaneProfileCatalog(Options(folder.Path), NullLogger<ControlPlaneProfileCatalog>.Instance);
        var profiles = await catalog.ListAsync(TestContext.Current.CancellationToken);

        var profile = profiles.ShouldHaveSingleItem();
        profile.Key.ShouldBe("implement");
        profile.Name.ShouldBe("Implementer");
        profile.Description.ShouldBe("writes the code");
        profile.AllowedTools.ShouldBe(["Read", "Bash"]);
        profile.Model.ShouldBe("heavy");
    }

    [Fact(DisplayName = "Given a document missing required frontmatter, when listed, then it is skipped")]
    public async Task SkipMalformedDocumentsAsync()
    {
        using var folder = new TempProfileFolder(
            ("broken", "---\ndescription: no name here\n---\nbody"),
            ("good", "---\nname: Good\ndescription: fine\n---\nbody"));

        var catalog = new ControlPlaneProfileCatalog(Options(folder.Path), NullLogger<ControlPlaneProfileCatalog>.Instance);

        (await catalog.ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem().Key.ShouldBe("good");
    }

    [Fact(DisplayName = "Given a missing folder, when listed, then the catalog is empty and GetAsync answers null")]
    public async Task AnswerEmptyForMissingFolderAsync()
    {
        var catalog = new ControlPlaneProfileCatalog(Options(Path.Combine(Path.GetTempPath(), "comuki-missing-profiles")), NullLogger<ControlPlaneProfileCatalog>.Instance);

        (await catalog.ListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await catalog.GetAsync("implement", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact(DisplayName = "Given listed profiles, when GetAsync is called with a key, then that profile comes back")]
    public async Task GetProfileByKeyAsync()
    {
        using var folder = new TempProfileFolder(
            ("implement", "---\nname: Implementer\ndescription: writes the code\n---\nbody"),
            ("explore", "---\nname: Explorer\ndescription: read-only recon\n---\nbody"));

        var catalog = new ControlPlaneProfileCatalog(Options(folder.Path), NullLogger<ControlPlaneProfileCatalog>.Instance);

        (await catalog.GetAsync("explore", TestContext.Current.CancellationToken)).ShouldNotBeNull().Name.ShouldBe("Explorer");
    }

    private static BrainOptions Options(string profilesPath)
    {
        return new BrainOptions { ControlPlaneProfilesPath = profilesPath };
    }

    private sealed class TempProfileFolder(params (string Key, string Content)[] documents) : IDisposable
    {
        public string Path { get; } = CreateFolder(documents);

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }

        private static string CreateFolder((string Key, string Content)[] documents)
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comuki-profiles-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(folder);
            foreach (var (key, content) in documents)
            {
                File.WriteAllText(System.IO.Path.Combine(folder, $"{key}.md"), content);
            }

            return folder;
        }
    }
}
