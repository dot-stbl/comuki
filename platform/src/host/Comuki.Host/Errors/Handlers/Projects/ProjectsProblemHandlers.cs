using Comuki.Modules.Projects.Application.Projects;
using Comuki.Modules.Projects.Application.Settings;

namespace Comuki.Host.Errors.Handlers.Projects;

// Wire rows for the Projects module's domain errors, taken verbatim from the
// retired per-module runner (Projects) arms they replace: status, title, detail sentences
// (including the retry suffix on the settings conflict) and the extensions
// the FE retry flow reads. Only the `type` URN changed spelling — it is now
// derived from the code (design D4) instead of hand-written per catch arm.

/// <summary>Project row absent → 404 with the requested <c>projectId</c> riding along.</summary>
internal sealed class ProjectNotFoundProblemHandler()
    : ProblemHandler<ProjectNotFoundException>(StatusCodes.Status404NotFound, "Project not found")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProjectNotFoundException exception)
    {
        return Row(
            exception.Message,
            exception.Code,
            new Dictionary<string, object?> { ["projectId"] = exception.ProjectId.ToString() });
    }
}

/// <summary>Stale settings version → 409 with <c>projectId</c> + the stored <c>currentVersion</c>, so the client can re-read and retry.</summary>
internal sealed class ProjectSettingsConflictProblemHandler()
    : ProblemHandler<ProjectSettingsConflictException>(StatusCodes.Status409Conflict, "Settings version conflict")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProjectSettingsConflictException exception)
    {
        return Row(
            exception.Message + "; re-read the settings and retry",
            exception.Code,
            new Dictionary<string, object?>
            {
                ["projectId"] = exception.ProjectId.ToString(),
                ["currentVersion"] = exception.CurrentVersion,
            });
    }
}

/// <summary>Project-level uniqueness violation (slug taken) → 409.</summary>
internal sealed class ProjectConflictProblemHandler()
    : ProblemHandler<ProjectConflictException>(StatusCodes.Status409Conflict, "Project conflict")
{
    /// <inheritdoc />
    protected override ProblemAnswer Answer(ProjectConflictException exception)
    {
        return Row(exception.Message, exception.Code);
    }
}
