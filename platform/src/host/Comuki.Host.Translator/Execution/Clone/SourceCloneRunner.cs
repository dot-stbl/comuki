using System.Text;

namespace Comuki.Host.Translator.Execution.Clone;

/// <summary>
/// Outcome of the workspace source clone: success, a missing
/// <c>SourceGitUrl</c> (workspace prepare requires it — fail the item,
/// do not start pi), or a failed clone (non-zero exit, launch failure,
/// or timeout — a private repository cloned without a credential lands
/// here through git's authentication error).
/// </summary>
public enum SourceCloneOutcomeKind
{
    /// <summary>The repository is cloned (or there was nothing to fail on yet).</summary>
    Succeeded,

    /// <summary>The claim carried no <c>SourceGitUrl</c> — workspace prepare fails the item.</summary>
    MissingUrl,

    /// <summary>The clone exited non-zero, failed to launch, or timed out.</summary>
    CloneFailed,
}

/// <summary>Clone outcome plus the reason the loop forwards to <c>POST /workers/{id}/fail</c>.</summary>
/// <param name="Kind">How the clone ended.</param>
/// <param name="Reason">Single-line failure reason; empty on success.</param>
/// <param name="RepositoryDirectory">The cloned repository root (empty on failure kinds).</param>
public sealed record SourceCloneOutcome(SourceCloneOutcomeKind Kind, string Reason, string RepositoryDirectory);

/// <summary>
/// Workspace source clone (harden-pi-worker-sandbox 4.3, design D4):
/// clones the claim's <c>SourceGitUrl</c> (HTTPS) at
/// <c>SourceGitRef</c> into a fixed subdirectory of the working
/// directory, <b>after claim and before pi</b>. A missing URL fails the
/// item — workspace prepare requires the product repository, the
/// profiles repository is not it.
/// <para>
/// <b>Credential handling.</b> The claim's resolved credential is
/// written to a throwaway git config file under the system temp
/// directory and pointed at via <c>GIT_CONFIG_GLOBAL</c> on the clone
/// child process only — it never appears in argv (visible in
/// <c>ps</c>), never in the Translator's own environment, never in the
/// agent's environment, and the file is deleted on every exit path. A
/// secret containing <c>:</c> is treated as <c>user:token</c>; a bare
/// token is wrapped as <c>x-access-token:token</c> (the GitHub PAT
/// convention) — both ride one <c>Authorization: Basic</c> extra
/// header scoped to the cloned URL's scheme+host.
/// </para>
/// <para>
/// <b>Warm slots.</b> The claim loop runs item after item in one
/// container; a leftover clone from the previous item is deleted
/// before the new clone starts. Raw commit-digest refs are not
/// supported by <c>clone --depth 1 --branch</c> and fail the item with
/// git's own error — branch and tag refs are the supported forms.
/// </para>
/// </summary>
/// <param name="processRunner">Process seam — a fake in tests.</param>
/// <param name="logger">Structured logger.</param>
public sealed class SourceCloneRunner(
    ISourceCloneProcessRunner processRunner,
    ILogger<SourceCloneRunner> logger)
{
    /// <summary>
    /// Clone target: fixed subdirectory under the working directory —
    /// constant so a hostile claim cannot steer the clone outside the
    /// workspace, and so downstream consumers (restore, exec, pi cwd)
    /// know where the repository root lives.
    /// </summary>
    public const string RepositoryDirectoryName = "source";

    /// <summary>Upper bound for one clone. Cold repo, slow registry — 10 minutes covers a depth-1 HTTPS clone.</summary>
    public static readonly TimeSpan CloneTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Executable per design D4 — git over HTTPS, no shell.</summary>
    public const string GitExecutable = "git";

    /// <summary>
    /// Clones <paramref name="sourceGitUrl"/> into
    /// <c>&lt;workingDirectory&gt;/source</c>. The returned kind tells
    /// the loop whether to proceed (<see cref="SourceCloneOutcomeKind.Succeeded"/>)
    /// or fail the item before pi starts.
    /// </summary>
    /// <param name="workingDirectory">The worker's working directory; the repository lands in <c>source</c> under it.</param>
    /// <param name="sourceGitUrl">Claim's HTTPS URL; <c>null</c>/whitespace → <see cref="SourceCloneOutcomeKind.MissingUrl"/>.</param>
    /// <param name="sourceGitRef">Branch or tag; <c>null</c>/whitespace clones the default branch.</param>
    /// <param name="gitCredential">Claim's resolved HTTPS credential; <c>null</c> clones anonymously.</param>
    /// <param name="cancellationToken">Cancels the step mid-clone.</param>
    public async Task<SourceCloneOutcome> RunAsync(
        string workingDirectory,
        string? sourceGitUrl,
        string? sourceGitRef,
        string? gitCredential,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceGitUrl))
        {
            const string reason = "workspace prepare requires SourceGitUrl — the project has no product repository configured.";
            logger.LogWarning("Refusing to prepare workspace: {Reason}", reason);
            return new SourceCloneOutcome(SourceCloneOutcomeKind.MissingUrl, reason, string.Empty);
        }

        var targetDirectory = Path.Combine(workingDirectory, RepositoryDirectoryName);
        SourceCloneCleanup.DeleteLeftover(targetDirectory, logger);

        string? credentialConfigPath = null;
        try
        {
            IReadOnlyDictionary<string, string>? environment = null;
            if (!string.IsNullOrWhiteSpace(gitCredential))
            {
                credentialConfigPath = await SourceCloneCredential.WriteConfigAsync(
                    sourceGitUrl, gitCredential, Path.GetTempPath(), cancellationToken);
                environment = new Dictionary<string, string>
                {
                    ["GIT_CONFIG_GLOBAL"] = credentialConfigPath,
                };
            }

            var arguments = BuildArguments(sourceGitUrl, sourceGitRef, targetDirectory);
            logger.LogInformation(
                "Cloning {Url} (ref {Ref}) into {Target}",
                sourceGitUrl,
                string.IsNullOrWhiteSpace(sourceGitRef) ? "<default>" : sourceGitRef,
                targetDirectory);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(CloneTimeout);
            var result = await processRunner.RunAsync(
                GitExecutable,
                arguments,
                environment,
                workingDirectory,
                timeoutCts.Token);

            if (result.ExitCode is not 0)
            {
                var detail = result.LaunchFailureDetail is { } launchFailure
                    ? launchFailure
                    : FirstLine(result.StandardError);
                var reason = $"git clone of '{sourceGitUrl}' failed: {detail}";
                logger.LogWarning("Source clone failed: {Reason}", reason);
                return new SourceCloneOutcome(SourceCloneOutcomeKind.CloneFailed, reason, string.Empty);
            }
        }
        finally
        {
            if (credentialConfigPath is not null)
            {
                SourceCloneCredential.Delete(credentialConfigPath, logger);
            }
        }

        return new SourceCloneOutcome(SourceCloneOutcomeKind.Succeeded, string.Empty, targetDirectory);
    }

    /// <summary>
    /// Builds the clone argv: <c>clone --depth 1 [--branch ref] url target</c>.
    /// The URL and ref ride argv unmodified (no credential anywhere in
    /// argv — that is the throwaway config file's job).
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string sourceGitUrl, string? sourceGitRef, string targetDirectory)
    {
        var arguments = new List<string> { "clone", "--depth", "1" };
        if (!string.IsNullOrWhiteSpace(sourceGitRef))
        {
            arguments.Add("--branch");
            arguments.Add(sourceGitRef);
        }

        arguments.Add(sourceGitUrl);
        arguments.Add(targetDirectory);
        return arguments;
    }

    /// <summary>First non-empty line of git's stderr — the quoted part of the fail reason.</summary>
    internal static string FirstLine(string standardError)
    {
        foreach (var line in standardError.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return "no stderr output";
    }
}

/// <summary>
/// File-scoped helpers for <see cref="SourceCloneRunner"/>: leftover
/// cleanup and the throwaway credential config (kept out of the class
/// per class-layout §1a — no private methods).
/// </summary>
file static class SourceCloneCleanup
{
    /// <summary>
    /// Deletes a leftover clone from a previous item on a warm slot.
    /// A file (not directory) at the path is deleted too — the clone
    /// owns the name entirely.
    /// </summary>
    public static void DeleteLeftover(string targetDirectory, ILogger logger)
    {
        try
        {
            if (File.Exists(targetDirectory))
            {
                File.Delete(targetDirectory);
            }
            else if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
                logger.LogInformation("Deleted leftover source clone at {Target}", targetDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The clone that follows fails with git's own "destination
            // path already exists" — same fail-the-item outcome, so the
            // cleanup failure needs no separate verdict here.
        }
    }
}

/// <summary>
/// The throwaway git credential config (design D4): one
/// <c>[http "scheme://host"]</c> section with an
/// <c>Authorization: Basic</c> extraHeader, written under the system
/// temp path, pointed at via <c>GIT_CONFIG_GLOBAL</c> on the clone
/// child only, deleted after the clone on every exit path.
/// </summary>
file static class SourceCloneCredential
{
    /// <summary>Secrets already in <c>user:token</c> form pass through; bare tokens get the GitHub PAT wrapper.</summary>
    public const string BareTokenUser = "x-access-token";

    /// <summary>Writes the throwaway config and returns its path. Caller deletes it via <see cref="Delete"/>.</summary>
    public static async Task<string> WriteConfigAsync(
        string sourceGitUrl,
        string credential,
        string tempRoot,
        CancellationToken cancellationToken)
    {
        var origin = OriginOf(sourceGitUrl);
        var header = BasicHeader(credential);
        var path = Path.Combine(tempRoot, $"comuki-git-{Guid.NewGuid():N}.config");
        var content = $$"""
                        [http "{{origin}}"]
                        extraHeader = "Authorization: Basic {{header}}"
                        """;
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }

    /// <summary>Best-effort delete of the throwaway config — on every exit path, never throws.</summary>
    public static void Delete(string path, ILogger logger)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Throwaway git credential config {Path} could not be deleted", path);
        }
    }

    /// <summary>
    /// Scheme+host the extraHeader scopes to — derived from the URL the
    /// claim carried; anything unparseable scopes to the whole URL
    /// string (git then matches nothing and the header is inert).
    /// </summary>
    internal static string OriginOf(string sourceGitUrl)
    {
        return Uri.TryCreate(sourceGitUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host)
            ? $"{uri.Scheme}://{uri.Host}"
            : sourceGitUrl;
    }

    /// <summary>
    /// Basic auth payload: <c>user:token</c> secrets pass through, bare
    /// tokens ride as <c>x-access-token:token</c> (GitHub PAT
    /// convention — git-over-HTTPS rejects Bearer).
    /// </summary>
    internal static string BasicHeader(string credential)
    {
        var pair = credential.Contains(':') ? credential : $"{BareTokenUser}:{credential}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));
    }
}
