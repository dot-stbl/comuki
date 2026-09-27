using System.Text.Json;
using Comuki.Modules.Projects.Application.Views;
using Comuki.Modules.Projects.Domain.Projects;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Wire-invariance pin (adopt-mapperly D6): the view records serialize the
/// exact same property-name set, in the same order, as they did as
/// positional records. The mapping migration must be invisible on the wire;
/// any drift here fails before the generated dashboard client can drift
/// with it (the kubb zero-diff gate is the second half of the pin).
/// </summary>
public sealed class ProjectViewWireShapeShould
{
    private readonly IProjectsMapper mapper = new ProjectsMapper();
    private readonly DateTimeOffset now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<string> projectViewProperties =
    [
        "id",
        "name",
        "slug",
        "description",
        "profilesGitUrl",
        "profilesGitRef",
        "icon",
        "color",
        "tags",
        "archived",
        "archivedAt",
        "createdAt",
        "updatedAt",
    ];

    private static readonly IReadOnlyList<string> projectSettingsViewProperties =
    [
        "projectId",
        "minIdle",
        "maxConcurrent",
        "idleTtlSeconds",
        "approveRequired",
        "knowledgeEnabled",
        "verifyEnabled",
        "proxyEnabled",
        "softBudgetUsdMicros",
        "hardBudgetUsdMicros",
        "domainType",
        "customDomainTypesJson",
        "updatedAt",
        "version",
    ];

    [Fact(DisplayName = "Given a fully-populated project view, when serialized with web options, then the property names and order match the pinned list")]
    public void ProjectViewSerializesThePinnedPropertySequence()
    {
        var project = Project.Create(
            "Web Platform",
            "web-platform",
            "customer portal",
            "https://git.example.com/acme/profiles.git",
            "refs/tags/v1",
            now,
            icon: "🛰️",
            color: "#3C5A86",
            tags: ["web"]);
        project.Archive(now.AddDays(1));

        var json = JsonSerializer.Serialize(mapper.ToView(project), JsonSerializerOptions.Web);
        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(static property => property.Name).ToArray();

        names.ShouldBe(projectViewProperties);
    }

    [Fact(DisplayName = "Given a fully-populated settings view, when serialized with web options, then the property names and order match the pinned list")]
    public void ProjectSettingsViewSerializesThePinnedPropertySequence()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), now);
        settings.Apply(2, 16, 1800, true, true, false, true, 2_000_000, 10_000_000, ProjectDomainType.Hybrid, "{}", now.AddMinutes(5));

        var json = JsonSerializer.Serialize(mapper.ToView(settings), JsonSerializerOptions.Web);
        var names = JsonDocument.Parse(json).RootElement.EnumerateObject().Select(static property => property.Name).ToArray();

        names.ShouldBe(projectSettingsViewProperties);
    }
}
