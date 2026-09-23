using System.Text.Json;
using Comuki.Modules.Verify.Domain.Ids;
using Comuki.Modules.Verify.Domain.Runs;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Verify.Infrastructure.Persistence;

/// <summary>
/// EF value converters for the module's strong ids, the Kernel
/// <see cref="ProjectId"/>, the <see cref="GenericCommandStatus"/>
/// smart-type, and the <see cref="GenericCommandRun.Arguments"/> array —
/// one place, so <c>GenericCommandRunConfiguration</c> has no inline
/// conversion logic.
/// </summary>
public static class VerifyValueConverters
{
    /// <summary><see cref="GenericCommandRunId"/> uuid converter.</summary>
    public static readonly ValueConverter<GenericCommandRunId, Guid> GenericCommandRunIdToUuid = new(
        static id => id.Value,
        static value => new GenericCommandRunId(value));

    /// <summary>Nullable <see cref="ProjectId"/> uuid converter — null stays null.</summary>
    public static readonly ValueConverter<ProjectId?, Guid?> ProjectIdToNullableUuid = new(
        static id => id.HasValue ? id.Value.Value : null,
        static value => value.HasValue ? new ProjectId(value.Value) : null);

    /// <summary>Bidirectional converter for the <see cref="GenericCommandStatus"/> smart-type.</summary>
    public static readonly ValueConverter<GenericCommandStatus, string> GenericCommandStatusToString = new(
        static status => status.Value,
        static wire => GenericCommandStatus.FromWire(wire));

    /// <summary>
    /// <see cref="GenericCommandRun.Arguments"/> round-tripped through a
    /// jsonb array column — a plain string list, no shape contract, so a
    /// converter is the right tool (not an owned type per
    /// <c>ef-owned-types.md</c>, which targets polymorphic/discriminated
    /// values). Serialization runs once per save/materialize, not on
    /// every property read.
    /// </summary>
    public static readonly ValueConverter<IReadOnlyList<string>, string> ArgumentsToJson = new(
        static arguments => JsonSerializer.Serialize(arguments, JsonSerializerOptions.Web),
        // boundary: these converter/comparer delegates compile to
        // expression trees for EF's query translator, which cannot
        // contain a collection expression (`[]`) — Array.Empty<string>()
        // is the expression-tree-safe equivalent.
        static json => JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonSerializerOptions.Web) ?? Array.Empty<string>());

    /// <summary>
    /// Value comparer for <see cref="ArgumentsToJson"/> — EF needs
    /// element-wise equality/hash to detect changes on a converted
    /// collection property instead of falling back to reference equality.
    /// </summary>
    public static readonly ValueComparer<IReadOnlyList<string>> ArgumentsComparer = new(
        static (left, right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>()),
        static value => value.Aggregate(0, static (hash, argument) => HashCode.Combine(hash, argument)),
        static value => value.ToList());
}
