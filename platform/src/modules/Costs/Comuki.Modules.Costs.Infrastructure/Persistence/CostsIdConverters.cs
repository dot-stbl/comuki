using Comuki.Modules.Costs.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Costs.Infrastructure.Persistence;

/// <summary>Value converters for Costs ids and Kernel scope ids.</summary>
public static class CostsIdConverters
{
    /// <summary><see cref="UsageEventId"/> uuid converter.</summary>
    public static readonly ValueConverter<UsageEventId, Guid> UsageEventIdToUuid = new(
        static id => id.Value,
        static value => new UsageEventId(value));

    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));

    /// <summary><see cref="RunId"/> uuid converter.</summary>
    public static readonly ValueConverter<RunId, Guid> RunIdToUuid = new(
        static id => id.Value,
        static value => new RunId(value));
}
