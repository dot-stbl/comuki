using System.Collections.Frozen;
using Comuki.Host.Auth.Models;
using Comuki.Modules.Identity.Application.Authorization;
using Comuki.Modules.Identity.Domain.Permissions;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Auth;

/// <summary>
/// Unit tests for the /me permission flattening: platform and project
/// permission sets become ordered wire strings keyed by project id.
/// </summary>
public sealed class MeResponseShould
{
    [Fact(DisplayName = "Given platform and project permissions, when flattened, then both scopes are ordered key strings keyed by project id")]
    public void FlattenPermissionsOrderedPerScope()
    {
        var project = ProjectId.New();
        var authorization = new SubjectAuthorization(
            new[] { Permissions.PlanRead, Permissions.RunRead }.ToFrozenSet(),
            new Dictionary<ProjectId, IReadOnlySet<PermissionKey>>
            {
                [project] = new[] { Permissions.ChatUse, Permissions.SourceWrite }.ToFrozenSet(),
            }.ToFrozenDictionary(static pair => pair.Key, static pair => pair.Value));

        var view = MeResponse.PermissionsView.From(authorization);

        view.Platform.ShouldBe(["plan:read", "run:read"]);
        view.Projects.Keys.ShouldBe([project.Value.ToString()]);
        view.Projects[project.Value.ToString()].ShouldBe(["chat:use", "source:write"]);
    }

    [Fact(DisplayName = "Given an empty authorization, when flattened, then both scopes are empty")]
    public void FlattenEmptyAuthorization()
    {
        var view = MeResponse.PermissionsView.From(SubjectAuthorization.Empty);

        view.Platform.ShouldBeEmpty();
        view.Projects.ShouldBeEmpty();
    }
}
