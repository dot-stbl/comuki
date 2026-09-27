using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Projects.Application.Projects;

/// <summary>
/// A project-level uniqueness constraint was violated (currently: the slug
/// is already taken); maps to HTTP 409.
/// </summary>
public sealed class ProjectConflictException(string message) : DomainException(ErrorCode, message)
{
    private const string ErrorCode = "project.conflict";
}
