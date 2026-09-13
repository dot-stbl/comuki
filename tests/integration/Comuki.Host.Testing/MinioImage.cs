namespace Comuki.Host.Testing;

/// <summary>
/// The MinIO image the artifact suites run against, named once.
/// </summary>
/// <remarks>
/// <para>
/// Fully qualified and pinned, and both halves are load-bearing.
/// </para>
/// <para>
/// <b>Why the registry is spelled out.</b> A bare <c>minio/minio</c> means
/// different things to different daemons: Docker resolves an unqualified
/// name against Docker Hub, Podman walks its own search list and finds
/// quay.io first. The suites passed locally under Podman and failed in CI
/// under Docker with <c>pull access denied … repository does not exist</c>,
/// because MinIO no longer publishes to Docker Hub under that name. Naming
/// the registry removes the ambiguity rather than relying on whichever
/// daemon happens to be installed.
/// </para>
/// <para>
/// <b>Why the tag is a release and not <c>latest</c>.</b> A moving tag makes
/// a green suite a statement about today rather than about the commit, and
/// it breaks retroactively: the run that passed last week cannot be
/// reproduced. This is the release the suites have actually been verified
/// against. Moving it is a deliberate change with its own test run, which is
/// the point.
/// </para>
/// </remarks>
public static class MinioImage
{
    /// <summary>Image reference passed to <c>MinioBuilder</c>.</summary>
    public const string Reference = "quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z";
}
