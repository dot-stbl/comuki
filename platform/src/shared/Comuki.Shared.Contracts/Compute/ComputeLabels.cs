using System.Text.RegularExpressions;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Label keys stamped on every worker container. Claim matching uses them:
/// a work item is claimable only when the worker's image digest and profiles
/// ref labels match the item's requirements.
/// </summary>
public static partial class ComputeLabels
{
    public const string Project = "comuki.project";
    public const string Profile = "comuki.profile";
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
