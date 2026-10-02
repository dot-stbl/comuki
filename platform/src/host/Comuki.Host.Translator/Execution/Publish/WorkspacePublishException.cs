namespace Comuki.Host.Translator.Execution.Publish;

/// <summary>
/// Typed failure of the workspace publish step (wave 2 of the GitLab e2e):
/// the working tree was dirty, the push failed, or (reserved) the MR create
/// HTTP call could not produce a 2xx. A dirty tree and a push failure fail
/// the work item; an MR HTTP failure is logged and swallowed — the branch
/// is already on the remote and the operator can open the MR by hand. The
/// <see cref="Code"/> is a stable dot.case machine id; <see cref="Exception.Message"/>
/// never contains the git credential.
/// </summary>
public sealed class WorkspacePublishException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The pi run produced uncommitted changes in the cloned product repo — refusing to push a dirty tree.</summary>
    public const string UncommittedCode = "workspace.uncommitted";

    /// <summary>The branch push to <c>origin</c> failed (auth, network, branch protection).</summary>
    public const string PushFailedCode = "workspace.push_failed";

    /// <summary>The GitLab merge request create call did not return 2xx — logged, not thrown (see <see cref="MergeRequestFailedCode"/> remarks).</summary>
    public const string MergeRequestFailedCode = "workspace.merge_request_failed";

    /// <summary>Stable dot.case id of the failure class.</summary>
    public string Code { get; } = code;
}
