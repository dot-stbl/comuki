using Comuki.Host.ControlPlane;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.ProfileCatalog;

/// <summary>
/// Unit tests for <see cref="ControlPlaneCatalog"/> against temp-dir
/// control-plane trees: ordering, key lookup, malformed-document tolerance,
/// and the dev-checkout root probe.
/// </summary>
public sealed class ControlPlaneCatalogShould
{
    private static ControlPlaneCatalog CreateCatalog(string root)
    {
        return new ControlPlaneCatalog(
            Options.Create(new ControlPlaneCatalogOptions { Root = root }),
            NullLogger<ControlPlaneCatalog>.Instance);
    }

    [Fact(DisplayName = "Given a profiles folder, when ListAsync, then profiles are ordered by key with metadata mapped")]
    public async Task ListProfilesOrderedByKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteProfile(
            "implement.md",
            """
            ---
            name: implement
            description: Implementation worker.
            allowedTools: [Read, Write, Bash]
            model: heavy
            ---

            Body.
            """);
        tree.WriteProfile(
            "explore-readonly.md",
            """
            ---
            name: explore-readonly
            description: Read-only explorer.
            allowedTools:
              - Read
              - Grep
            ---

            Body.
            """);

        var profiles = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        profiles.Select(static profile => profile.Key).ShouldBe(["explore-readonly", "implement"]);

        var explorer = profiles[0];
        explorer.Name.ShouldBe("explore-readonly");
        explorer.Description.ShouldBe("Read-only explorer.");
        explorer.AllowedTools.ShouldBe(["Read", "Grep"]);
        explorer.Model.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a malformed profile document, when ListAsync, then it is skipped and the rest of the catalog survives")]
    public async Task SkipMalformedDocumentsAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteProfile("good.md", "---\nname: good\ndescription: Valid.\n---\n\nBody.");
        tree.WriteProfile("broken.md", "# no frontmatter at all");
        tree.WriteProfile("nameless.md", "---\ndescription: Name is missing.\n---\n\nBody.");

        var profiles = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);

        profiles.ShouldHaveSingleItem().Key.ShouldBe("good");
    }

    [Fact(DisplayName = "Given an existing profile key, when GetAsync, then the profile is returned")]
    public async Task GetProfileByKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteProfile("implement.md", "---\nname: implement\ndescription: Implementation worker.\n---\n\nBody.");

        var profile = await CreateCatalog(tree.Root).GetAsync("implement", TestContext.Current.CancellationToken);

        _ = profile.ShouldNotBeNull();
        profile.Key.ShouldBe("implement");
        profile.Description.ShouldBe("Implementation worker.");
    }

    [Fact(DisplayName = "Given an unknown profile key, when GetAsync, then returns null")]
    public async Task ReturnNullForUnknownKeyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteProfile("implement.md", "---\nname: implement\ndescription: Implementation worker.\n---\n\nBody.");

        var profile = await CreateCatalog(tree.Root).GetAsync("does-not-exist", TestContext.Current.CancellationToken);

        profile.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a chat-commands folder, when ListCommandsAsync, then commands carry key, description and body")]
    public async Task ListChatCommandsWithBodyAsync()
    {
        using var tree = new TempControlPlane();
        tree.WriteChatCommand(
            "init.md",
            """
            ---
            name: init
            description: Onboard a project.
            ---

            Run the onboarding wizard.
            """);

        var commands = await CreateCatalog(tree.Root).ListCommandsAsync(TestContext.Current.CancellationToken);

        var command = commands.ShouldHaveSingleItem();
        command.Key.ShouldBe("init");
        command.Name.ShouldBe("init");
        command.Description.ShouldBe("Onboard a project.");
        command.Body.ShouldContain("onboarding wizard");
    }

    [Fact(DisplayName = "Given a root without the catalog folder, when listing, then the catalog is empty and nothing throws")]
    public async Task ReturnEmptyWhenFolderMissingAsync()
    {
        using var tree = new TempControlPlane();
        _ = Directory.CreateDirectory(tree.Root);

        var profiles = await CreateCatalog(tree.Root).ListAsync(TestContext.Current.CancellationToken);
        var commands = await CreateCatalog(tree.Root).ListCommandsAsync(TestContext.Current.CancellationToken);

        profiles.ShouldBeEmpty();
        commands.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a control-plane directory above the start directory, when probing, then the root is found")]
    public void ProbeControlPlaneRootFromDescendant()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "comuki-probe-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(baseDir, ControlPlaneCatalog.RootFolderName);
        _ = Directory.CreateDirectory(Path.Combine(root, ControlPlaneCatalog.ProfilesFolder));
        var deep = Path.Combine(baseDir, "a", "b", "c");
        _ = Directory.CreateDirectory(deep);

        var found = ControlPlaneCatalog.ProbeControlPlaneRoot(deep);

        found.ShouldBe(root);
        Directory.Delete(baseDir, recursive: true);
    }

    [Fact(DisplayName = "Given no control-plane directory upwards, when probing, then returns null")]
    public void ProbeControlPlaneRootReturnsNullWhenAbsent()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "comuki-probe-" + Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(baseDir, "plain", "tree");
        _ = Directory.CreateDirectory(deep);

        var found = ControlPlaneCatalog.ProbeControlPlaneRoot(deep);

        found.ShouldBeNull();
        Directory.Delete(baseDir, recursive: true);
    }
}
