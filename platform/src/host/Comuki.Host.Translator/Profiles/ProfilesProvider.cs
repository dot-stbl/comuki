using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Host.Translator.Profiles;

/// <summary>
/// v0 profiles provider: copies from a mounted local directory
/// (<c>COMUKI_PROFILES_PATH</c>) when set, else clones a public git repo
/// (<c>COMUKI_PROFILES_GIT_URL</c>) at the pinned ref (no credentials in
/// v0), else logs a warning and skips. Material lands under
/// <c>profiles/</c> in the working directory.
/// </summary>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class ProfilesProvider(
    IOptions<TranslatorOptions> options,
    ILogger<ProfilesProvider> logger) : IProfilesProvider
{
    private const string ProfilesDirectoryName = "profiles";

    /// <inheritdoc />
    public async Task PrepareAsync(string profilesRef, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var target = Path.Combine(opts.WorkingDirectory, ProfilesDirectoryName);
        if (Directory.Exists(target))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(opts.ProfilesPath))
        {
            logger.LogInformation("Copying profiles from {Source} to {Target}", opts.ProfilesPath, target);
            ProfilesFileSystem.CopyDirectory(opts.ProfilesPath, target);
            return;
        }

        if (opts.ProfilesGitUrl is { } gitUrl)
        {
            logger.LogInformation("Cloning profiles {Url}@{Ref} to {Target}", gitUrl, profilesRef, target);
            await Task.Run(() => ProfilesFileSystem.Clone(gitUrl, profilesRef, target), cancellationToken);
            return;
        }

        logger.LogWarning("No profiles source configured (COMUKI_PROFILES_PATH / COMUKI_PROFILES_GIT_URL) — running with platform defaults");
    }
}

/// <summary>Profiles materialization helpers: local copy + public-repo shallow clone at a pinned ref.</summary>
internal static class ProfilesFileSystem
{
    public static void CopyDirectory(string source, string target)
    {
        _ = Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            _ = Directory.CreateDirectory(Path.Combine(target, directory[source.Length..].TrimStart(Path.DirectorySeparatorChar)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, file[source.Length..].TrimStart(Path.DirectorySeparatorChar)), overwrite: true);
        }
    }

    /// <summary>Shallow clone of a public repo with the pinned ref checked out (no credentials in v0).</summary>
    public static void Clone(Uri gitUrl, string profilesRef, string target)
    {
        var cloneOptions = new CloneOptions();
        var repositoryPath = Repository.Clone(gitUrl.ToString(), target, cloneOptions);
        using var repository = new Repository(repositoryPath);
        Commands.Checkout(repository, profilesRef, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force });
    }
}
