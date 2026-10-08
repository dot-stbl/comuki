using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Projects.Archive;

/// <summary>Soft-archives a project (the row, its runs and settings are kept).</summary>
public sealed record ArchiveProjectCommand(ProjectId ProjectId);
