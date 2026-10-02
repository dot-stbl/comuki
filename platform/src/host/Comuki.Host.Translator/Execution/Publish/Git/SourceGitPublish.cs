namespace Comuki.Host.Translator.Execution.Publish.Git;

/// <summary>One push of the cloned product repository for a claimed work item.</summary>
/// <param name="WorkingDirectory">The cloned tree pi ran in — git's working directory for every operation.</param>
/// <param name="Branch">
/// The branch to check out and push — <c>comuki/{run:N}/{item:N}</c>. Always created
/// from the current <c>HEAD</c>; the branch name is the only thing that crosses
/// this seam, never the credential.
/// </param>
/// <param name="CredentialConfigPath">
/// Path of the throwaway git config carrying the <c>http.extraHeader</c>
/// credential; null pushes without auth. The path, not the credential, is
/// what crosses the seam.
/// </param>
/// <param name="OriginRef">
/// Branch on origin that the publisher compares <c>HEAD</c> against — the
/// <c>SourceGitRef</c> with the <c>refs/heads/</c> prefix stripped (or
/// null when no ref was pinned, in which case <c>origin/HEAD</c> is the
/// compare target).
/// </param>
public sealed record SourceGitPublish(
    string WorkingDirectory,
    string Branch,
    string? CredentialConfigPath,
    string? OriginRef);
