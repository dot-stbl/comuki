using Comuki.Shared.Kernel.Exceptions;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Settings;

/// <summary>
/// The settings row was written by someone else since the caller read it
/// (presented version is not current + 1, or a concurrent writer won the
/// race). The client should re-read and retry; maps to HTTP 409 with the
/// stored version riding along as <c>currentVersion</c>.
/// </summary>
/// <param name="projectId">Project whose settings were contested.</param>
/// <param name="expectedVersion">Version the writer presented.</param>
/// <param name="currentVersion">Version actually stored at write time.</param>
/// <param name="innerException">Optional root cause for log-only use.</param>
public sealed class ProjectSettingsConflictException(
    ProjectId projectId,
    int expectedVersion,
    int currentVersion,
    Exception? innerException = null)
    : DomainException(
        ErrorCode,
        $"settings of project {projectId} changed: expected version {expectedVersion}, current version {currentVersion}",
        innerException)
{
    private const string ErrorCode = "project.settings_conflict";

    /// <summary>Project whose settings were contested.</summary>
    public ProjectId ProjectId { get; } = projectId;

    /// <summary>Version the writer presented.</summary>
    public int ExpectedVersion { get; } = expectedVersion;

    /// <summary>Version actually stored at write time.</summary>
    public int CurrentVersion { get; } = currentVersion;
}
