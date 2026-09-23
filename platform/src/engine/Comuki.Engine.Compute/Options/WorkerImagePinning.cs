using Comuki.Shared.Bootstrap.Versioning;

namespace Comuki.Engine.Compute.Options;

/// <summary>
/// Derives the effective worker image reference for a spawn (release
/// contract, RELEASE.md: the worker image version must match the host
/// version). An image configured WITHOUT a tag is pinned to the running
/// build's version; an unstamped build — <see cref="ComukiBuildInformation.Unknown"/>,
/// e.g. a local <c>dotnet build</c> or an unstamped container image —
/// falls back to <see cref="LatestTag"/>. An explicit tag or digest is
/// never modified: operator overrides always win.
///
/// Shared by BOTH sides of claim matching: the scale supervisor pins the
/// image it spawns, and the run starters (chat/intake/scheduler) pin the
/// image they stamp on queued work items — the claim SQL compares the two
/// for equality, so they must resolve through this one function.
/// </summary>
public static class WorkerImagePinning
{
    /// <summary>Tag used when the running build carries no release version.</summary>
    public const string LatestTag = "latest";

    /// <summary>
    /// Resolves the image to spawn: unchanged when it already carries a tag
    /// or a digest; <c>{image}:{version}</c> when untagged, with the tag
    /// falling back to <see cref="LatestTag"/> for an unstamped build.
    /// </summary>
    /// <param name="configuredImage">Configured worker image (options default or per-project override).</param>
    /// <param name="buildInformation">Build identity of the running host.</param>
    public static string Resolve(string configuredImage, ComukiBuildInformation buildInformation)
    {
        return HasTagOrDigest(configuredImage)
            ? configuredImage
            : $"{configuredImage}:{TagFor(buildInformation)}";
    }

    /// <summary>
    /// True when the reference pins itself: carries a digest
    /// (<c>repo@sha256:…</c>) or a tag on the final path segment
    /// (<c>repo:v1</c>). Colons inside the registry host
    /// (<c>registry:5000/repo</c>) are ports, not tags.
    /// </summary>
    /// <param name="image">Raw configured image reference.</param>
    internal static bool HasTagOrDigest(string image)
    {
        return image.Contains('@') || image[(image.LastIndexOf('/') + 1)..].Contains(':');
    }

    /// <summary>Tag derived from the build version: leading 'v' stripped, <see cref="LatestTag"/> when unstamped or empty.</summary>
    /// <param name="buildInformation">Build identity of the running host.</param>
    internal static string TagFor(ComukiBuildInformation buildInformation)
    {
        var version = buildInformation.Version.Trim();
        if (version.StartsWith('v') || version.StartsWith('V'))
        {
            version = version[1..];
        }

        return version.Length == 0 || version == ComukiBuildInformation.Unknown.Version
            ? LatestTag
            : version;
    }
}
