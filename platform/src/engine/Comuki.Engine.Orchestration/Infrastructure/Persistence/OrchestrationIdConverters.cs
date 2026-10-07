using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Engine.Orchestration.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping Kernel id value objects to <c>uuid</c> columns.
/// EF sees a <see cref="Guid"/>; the domain sees the strong-typed id.
/// </summary>
internal static class OrchestrationIdConverters
{
    /// <summary><see cref="RunId"/> uuid converter.</summary>
    public static readonly ValueConverter<RunId, Guid> RunIdToUuid = new(
        static id => id.Value,
        static value => new RunId(value));

    /// <summary>
    /// <see cref="RunId"/> nullable uuid converter — for columns where the
    /// run reference is optional (Coda phase: <c>MergeQueueEntry.RunId</c>,
    /// <c>MergeBatch.RunId</c>). The <c>GenericCommandRun.WorkItemId</c>
    /// column is a plain <c>Guid?</c> (not a <c>RunId?</c>) — the engine
    /// has no strong-typed <c>WorkItemId</c> value object, so that column
    /// takes EF's default mapping without a custom converter.
    /// </summary>
    public static readonly ValueConverter<RunId?, Guid?> RunIdToNullableUuid = new(
        static id => id.HasValue ? id.Value.Value : null,
        static value => value.HasValue ? new RunId(value.Value) : null);

    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));

    /// <summary><see cref="WorkerId"/> uuid converter.</summary>
    public static readonly ValueConverter<WorkerId, Guid> WorkerIdToUuid = new(
        static id => id.Value,
        static value => new WorkerId(value));
}
