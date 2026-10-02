namespace Comuki.Host.Translator.Execution.Workspace.Git;

/// <summary>One product-repository checkout for a claimed work item.</summary>
/// <param name="Url">Absolute HTTPS URL of the repository to clone.</param>
/// <param name="Ref">Branch or tag to check out (depth 1); null = the default branch.</param>
/// <param name="TargetDirectory">Directory the tree lands in — the working directory pi will run in.</param>
/// <param name="CredentialConfigPath">
/// Path of the throwaway git config carrying the
/// <c>http.extraHeader</c> credential; null clones without auth (public
/// repositories). The path, not the credential, is what crosses the seam.
/// </param>
public sealed record SourceGitClone(
    Uri Url,
    string? Ref,
    string TargetDirectory,
    string? CredentialConfigPath);
