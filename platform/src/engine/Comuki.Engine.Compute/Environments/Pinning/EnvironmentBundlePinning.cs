namespace Comuki.Engine.Compute.Environments.Pinning;

/// <summary>
/// Catalog-side equivalent of <c>WorkerImagePinning</c>: the single
/// source of truth the worker-start gate calls to decide whether an
/// <see cref="EnvironmentBundle.Image"/> reference is acceptable. A
/// reference carrying a digest (<c>repo@sha256:…</c>) is always
/// startable; a tag-only or untagged reference is startable in dev /
/// stage (the local-experience path) and refused in
/// <c>Production</c> — the digest pins the worker image to a fixed
/// revision so a rollout cannot silently swap bytes under a running
/// fleet (worker-environments spec §"Production refuses a tag-only bundle").
///
/// This file mirrors the WorkerImagePinning surface — an internal
/// predicate (<see cref="HasDigest"/>) plus a <c>Production</c>-aware
/// gate (<see cref="IsStartable"/>) — so the gate that the
/// <c>ComputeStartRequest</c> path eventually adds (task 3.3, out of
/// scope for this slice) is a one-liner.
/// </summary>
public static class EnvironmentBundlePinning
{
    /// <summary>
    /// True when the image reference carries a digest
    /// (<c>repo@sha256:…</c>). The image is then pinned to a fixed
    /// revision and safe to start in <c>Production</c>. Single home of
    /// the digest predicate — <see cref="EnvironmentBundle.HasDigest"/>
    /// delegates here.
    /// </summary>
    /// <param name="image">Raw configured image reference.</param>
    /// <returns>True when the reference includes a digest.</returns>
    internal static bool HasDigest(string image)
    {
        return image.Contains('@');
    }

    /// <summary>
    /// Production gate: returns <c>false</c> when the reference is
    /// tag-only and the caller is in <c>Production</c>. Digest-pinned
    /// references are always startable; tag-only references are
    /// startable in non-Production environments (the dev / stage path).
    /// </summary>
    /// <param name="image">Bundle's image reference.</param>
    /// <param name="isProduction">True when the calling host is <c>Production</c>.</param>
    /// <returns>True when the worker-start gate may proceed.</returns>
    internal static bool IsStartable(string image, bool isProduction)
    {
        return HasDigest(image) || !isProduction;
    }
}
