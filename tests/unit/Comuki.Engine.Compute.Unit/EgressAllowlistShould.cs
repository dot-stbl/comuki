using Comuki.Engine.Compute.Egress;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Locks the pure egress-allowlist intersection: a profile or project may
/// only NARROW the deployment default, a caller-added host (Brain naming
/// an extra destination through the profile) is dropped, and an empty
/// deployment default means no egress at all (fail-closed).
/// </summary>
public sealed class EgressAllowlistShould
{
    private static readonly IReadOnlyList<string> deploymentDefault =
        ["comuki-proxy.internal", "nexus.internal", "github.com"];

    [Fact(DisplayName = "When neither Project Nor Profile Narrows, then deployment default passes through")]
    public void PassdeploymentDefaultThroughWhenNothingNarrows()
    {
        var effective = EgressAllowlist.Resolve(deploymentDefault, projectHosts: null, profileHosts: null);

        effective.ShouldBe(deploymentDefault);
    }

    [Fact(DisplayName = "Given empty project and profile lists, when resolving, then no narrowing happens")]
    public void TreatEmptyListsAsNoNarrowing()
    {
        var effective = EgressAllowlist.Resolve(deploymentDefault, projectHosts: [], profileHosts: []);

        effective.ShouldBe(deploymentDefault);
    }

    [Fact(DisplayName = "When a Profile Narrows, then only the named default hosts remain")]
    public void NarrowByProfile()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentDefault,
            projectHosts: null,
            profileHosts: ["comuki-proxy.internal"]);

        effective.ShouldBe(["comuki-proxy.internal"]);
    }

    [Fact(DisplayName = "When a Project Narrows, then only the named default hosts remain")]
    public void NarrowByProject()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentDefault,
            projectHosts: ["nexus.internal", "github.com"],
            profileHosts: null);

        effective.ShouldBe(["nexus.internal", "github.com"]);
    }

    [Fact(DisplayName = "Given profile and project lists, when resolving, then the intersection of all three applies")]
    public void IntersectProjectAndProfile()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentDefault,
            projectHosts: ["comuki-proxy.internal", "nexus.internal"],
            profileHosts: ["nexus.internal", "github.com"]);

        // profile may only narrow the project-level set — github.com is
        // gone because the project already dropped it.
        effective.ShouldBe(["nexus.internal"]);
    }

    [Fact(DisplayName = "Given a caller-added host (Brain naming an extra destination), when resolving, then it is dropped")]
    public void DropCallerAddedHosts()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentDefault,
            projectHosts: null,
            profileHosts: ["comuki-proxy.internal", "evil.example.com", "169.254.169.254"]);

        effective.ShouldBe(["comuki-proxy.internal"]);
    }

    [Fact(DisplayName = "Given host names differing in case, when resolving, then comparison is case-insensitive and default casing wins")]
    public void CompareHostsCaseInsensitively()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentDefault,
            projectHosts: null,
            profileHosts: ["COMUKI-PROXY.internal", "GITHUB.com"]);

        effective.ShouldBe(["comuki-proxy.internal", "github.com"]);
    }

    [Fact(DisplayName = "Given an empty deployment default, when resolving, then the allowlist is empty (fail-closed)")]
    public void EmptydeploymentDefaultMeansNoEgress()
    {
        var effective = EgressAllowlist.Resolve(
            deploymentHosts: [],
            projectHosts: ["comuki-proxy.internal"],
            profileHosts: null);

        effective.ShouldBeEmpty();
    }
}
