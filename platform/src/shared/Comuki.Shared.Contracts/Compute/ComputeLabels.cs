namespace Comuki.Shared.Contracts.Compute;

/// <summary>
/// Label keys stamped on every worker container. Claim matching uses them:
/// a work item is claimable only when the worker's image digest and profiles
/// ref labels match the item's requirements.
/// </summary>
public static class ComputeLabels
{
    public const string Project = "comuki.project";
    public const string Profile = "comuki.profile";
    public const string Image = "comuki.image";
    public const string ProfilesRef = "comuki.profiles_ref";

    /// <summary>Kubernetes label values must match [A-Za-z0-9._-]; git refs with
    /// slashes become underscores.</summary>
    /// <param name="value"></param>
    public static string Sanitize(string value)
    {
        return value.Replace('/', '_');
    }
}
