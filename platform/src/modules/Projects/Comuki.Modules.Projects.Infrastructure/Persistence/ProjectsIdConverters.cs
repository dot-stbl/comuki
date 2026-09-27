using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Projects.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the Kernel <see cref="ProjectId"/> and the
/// module's own ids to <c>uuid</c> columns (projects themselves have no id
/// of their own — projects ARE the Kernel scope unit). The
/// <see cref="RepositoryId"/> converter lives here too because the
/// attachment's repository column stores the sibling Repositories module's
/// aggregate id — same <see cref="Guid"/> bytes, but the Projects module
/// owns its own value type by the modular-monolith sibling-isolation law.
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

    /// <summary><see cref="ProjectRepositoryAttachmentId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectRepositoryAttachmentId, Guid> ProjectRepositoryAttachmentIdToUuid = new(
        static id => id.Value,
        static value => new ProjectRepositoryAttachmentId(value));

    /// <summary><see cref="RepositoryId"/> uuid converter (Projects-side value type).</summary>
    public static readonly ValueConverter<RepositoryId, Guid> RepositoryIdToUuid = new(
        static id => id.Value,
        static value => new RepositoryId(value));
}
