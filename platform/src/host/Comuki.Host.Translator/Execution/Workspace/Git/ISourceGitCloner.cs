namespace Comuki.Host.Translator.Execution.Workspace.Git;

/// <summary>
/// The git seam of the workspace prepare step (issue #125): one one-shot
/// clone invocation — no long-lived processes, no daemon. Unit tests
/// substitute a fake; the production implementation shells out to
/// <c>git clone --depth 1</c>.
/// </summary>
public interface ISourceGitCloner
{
    /// <summary>
    /// Clones <paramref name="clone"/>.Url at the pinned ref into the
    /// target directory. Throws <see cref="WorkspacePrepareException"/>
    /// with <see cref="WorkspacePrepareException.CloneFailedCode"/> when
    /// git exits non-zero (bad ref, auth, network).
    /// </summary>
    /// <param name="clone">The checkout to perform.</param>
    /// <param name="cancellationToken"></param>
    public Task CloneAsync(SourceGitClone clone, CancellationToken cancellationToken = default);
}
