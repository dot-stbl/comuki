namespace Comuki.Shared.Kernel.Harness;

/// <summary>
/// Stable identifiers for the registered <see cref="IHarness"/>
/// implementations. The constants are the wire values the
/// <c>control-plane/profiles/&lt;name&gt;.md</c> <c>harness:</c>
/// frontmatter matches against and the orchestrator stores on the
/// claim label. The values are stable across the wire — renaming
/// is a breaking change for any profile frontmatter that pins a
/// specific harness.
/// </summary>
public static class HarnessIds
{
    /// <summary>Production harness name. Default when the profile frontmatter omits <c>harness:</c>.</summary>
    public const string Pi = "pi";

    /// <summary>In-process test fake harness name. Same name as the external <c>Comuki.TestFakePi</c> binary so claim-matching is uniform across unit tests, integration tests, and the docker-compose worker image.</summary>
    public const string TestFakePi = "test-fake-pi";
}
