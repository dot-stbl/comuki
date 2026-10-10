using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the Kernel <see cref="ProjectId"/> and the
/// module's own ids to <c>uuid</c> columns (projects themselves have no id
/// of their own — projects ARE the Kernel scope unit).
/// </summary>
public static class ProjectsIdConverters
{
    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));

    /// <summary><see cref="DomainTypeAdmissionId"/> uuid converter.</summary>
    public static readonly ValueConverter<DomainTypeAdmissionId, Guid> DomainTypeAdmissionIdToUuid = new(
        static id => id.Value,
        static value => new DomainTypeAdmissionId(value));
}
