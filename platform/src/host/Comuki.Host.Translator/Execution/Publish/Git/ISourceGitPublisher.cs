namespace Comuki.Host.Translator.Execution.Publish.Git;

/// <summary>
/// The git-binary seam of the workspace publish step (wave 2 of the GitLab
/// e2e): a status check, a rev-parse against <c>HEAD</c> and the origin
/// ref, a branch creation, and a push. Unit tests substitute a fake; the
/// production implementation shells out to <c>git</c>.
/// </summary>
public interface ISourceGitPublisher
{
    /// <summary><c>git status --porcelain</c> stdout (empty = clean). Throws <see cref="WorkspacePublishException"/> with <see cref="WorkspacePublishException.PushFailedCode"/> when git itself fails (binary missing, not a repo).</summary>
    /// <param name="publish">The push being prepared.</param>
    /// <param name="cancellationToken"></param>
    public Task<string> ReadStatusAsync(SourceGitPublish publish, CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>git rev-parse {spec}</c> stdout trimmed. Returns <c>null</c> when
    /// git exits non-zero (unknown ref) rather than throwing — a missing
    /// origin SHA is treated as "has new commits" by the publisher and
    /// the push runs anyway.
    /// </summary>
    /// <param name="publish">The push being prepared.</param>
    /// <param name="spec">A rev-parse spec: <c>"HEAD"</c>, <c>"origin/main"</c>, <c>"origin/HEAD"</c>.</param>
    /// <param name="cancellationToken"></param>
    public Task<string?> RevParseAsync(SourceGitPublish publish, string spec, CancellationToken cancellationToken = default);

    /// <summary><c>git checkout -B {publish.Branch}</c>. Throws <see cref="WorkspacePublishException"/> with <see cref="WorkspacePublishException.PushFailedCode"/> on non-zero.</summary>
    /// <param name="publish">The push being prepared.</param>
    /// <param name="cancellationToken"></param>
    public Task CheckoutBranchAsync(SourceGitPublish publish, CancellationToken cancellationToken = default);

    /// <summary><c>git push -u origin {publish.Branch}</c>. Throws <see cref="WorkspacePublishException"/> with <see cref="WorkspacePublishException.PushFailedCode"/> on non-zero.</summary>
    /// <param name="publish">The push being prepared.</param>
    /// <param name="cancellationToken"></param>
    public Task PushAsync(SourceGitPublish publish, CancellationToken cancellationToken = default);
}
