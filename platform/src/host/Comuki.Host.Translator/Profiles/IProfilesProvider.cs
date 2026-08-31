namespace Comuki.Host.Translator.Profiles;

/// <summary>
/// Prepares the worker's profile material before a run: client system
/// prompts / skills from a mounted directory or a public git repo. v0 is
/// best-effort — a worker without a configured source runs with the
/// platform defaults and logs a warning.
/// </summary>
public interface IProfilesProvider
{
    /// <summary>Materializes the profiles of the given ref into the working directory.</summary>
    /// <param name="profilesRef"></param>
    /// <param name="cancellationToken"></param>
    public Task PrepareAsync(string profilesRef, CancellationToken cancellationToken = default);
}
