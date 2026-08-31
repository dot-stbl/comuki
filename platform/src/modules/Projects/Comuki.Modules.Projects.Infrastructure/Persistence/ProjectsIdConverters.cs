using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the Kernel <see cref="ProjectId"/> to a
/// <c>uuid</c> column (the module has no ids of its own — projects ARE the
/// Kernel scope unit).
/// </summary>
public static class ProjectsIdConverters
{
    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));
}
