using System.Text.RegularExpressions;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Label keys stamped on every worker container. Claim matching uses them:
/// a work item is claimable only when the worker's environment class,
/// image digest and profiles ref labels match the item's requirements.
/// </summary>
public static partial class ComputeLabels
{
    public const string Project = "comuki.project";
    public const string Profile = "comuki.profile";

    /// <summary>
    /// Claim-matching environment-class label. A worker labelled
    /// <c>comuki.env_class=net10-sdk-bun</c> only satisfies backlog of the
    /// same class — idle UE workers do not satisfy a net10 backlog
    /// (worker-environments spec §"Idle net10 worker does not match UE backlog").
    /// </summary>
    public const string EnvClass = "comuki.env_class";

    /// <summary>
    /// Diagnostics-only label carrying the resolved image digest. Claim
    /// matching does NOT select on this label — the env class is the
    /// claim key. Stamped for operators who want to verify the started
    /// image matches the bound class.
    /// </summary>
    public const string Image = "comuki.image";

    public const string ProfilesRef = "comuki.profiles_ref";

    /// <summary>Kubernetes label values must match <c>[A-Za-z0-9._-]</c>
    /// and start/end alphanumeric; docker image references carry
    /// <c>:</c> and git refs carry <c>/</c>. Both the stamp side (spawn)
    /// and the read side (list / claim matching) run through this one
    /// function, so the mapping only has to be deterministic, not
    /// reversible.</summary>
    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex InvalidLabelChars();

    /// <summary>Replaces every character a Kubernetes label value cannot
    /// carry with <c>_</c> and trims leading/trailing ones (a label value
    /// must start and end with an alphanumeric).</summary>
    /// <param name="value"></param>
    public static string Sanitize(string value)
    {
        return InvalidLabelChars().Replace(value, "_").Trim('_');
    }
}
