namespace Comuki.Host.Workers.Workspace;

/// <summary>
/// The workspace facts a claim hands to the worker: where the product
/// repository lives, at what ref, and the resolved HTTPS credential — or
/// no credential for public repositories.
/// </summary>
/// <param name="SourceGitUrl">HTTPS git URL of the claimed item's product repository.</param>
/// <param name="SourceGitRef">Pinned branch/tag/digest; null = the repository's default branch.</param>
/// <param name="GitCredential">
/// Resolved credential value for the clone; null when the project
/// configured none (public repositories) or the reference could not be
/// resolved (the clone then fails unauthenticated — see the resolver).
/// Appears exactly once, in the claim response — never journaled or logged.
/// </param>
public sealed record ClaimWorkspace(
    string SourceGitUrl,
    string? SourceGitRef,
    string? GitCredential);
