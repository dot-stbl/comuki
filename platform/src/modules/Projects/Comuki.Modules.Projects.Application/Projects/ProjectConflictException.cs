namespace Comuki.Modules.Projects.Application.Projects;

/// <summary>
/// A project-level uniqueness constraint was violated (currently: the slug
/// is already taken); maps to HTTP 409.
/// </summary>
/// <param name="message"></param>
public sealed class ProjectConflictException(string message) : Exception(message);
