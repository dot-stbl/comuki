using Comuki.Modules.Scheduler.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Scheduler.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the module's strong ids (and the Kernel
/// <see cref="ProjectId"/>) to <c>uuid</c> columns.
/// </summary>
public static class SchedulerIdConverters
{
    /// <summary><see cref="ScheduledJobId"/> uuid converter.</summary>
    public static readonly ValueConverter<ScheduledJobId, Guid> ScheduledJobIdToUuid = new(
        static id => id.Value,
        static value => new ScheduledJobId(value));

    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));
}
