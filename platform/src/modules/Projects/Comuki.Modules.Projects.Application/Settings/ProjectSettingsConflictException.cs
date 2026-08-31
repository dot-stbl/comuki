using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Settings;

/// <summary>
/// The settings row was written by someone else since the caller read it
/// (presented version is not current + 1, or a concurrent writer won the
/// race). The client should re-read and retry; maps to HTTP 409.
/// </summary>
/// <param name="projectId"></param>
/// <param name="expectedVersion"></param>
/// <param name="currentVersion"></param>
/// <param name="innerException"></param>
public sealed class ProjectSettingsConflictException(
    ProjectId projectId,
    int expectedVersion,
    int currentVersion,
    Exception? innerException = null)
    : Exception(
        $"settings of project {projectId} changed: expected version {expectedVersion}, current version {currentVersion}",
        innerException)
{
    /// <summary>Project whose settings were contested.</summary>
    public ProjectId ProjectId { get; } = projectId;

    /// <summary>Version the writer presented.</summary>
    public int ExpectedVersion { get; } = expectedVersion;

    /// <summary>Version actually stored at write time.</summary>
    public int CurrentVersion { get; } = currentVersion;
}
