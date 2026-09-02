using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Intake.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the module's strong ids (and the Kernel
/// <see cref="RunId"/>) to <c>uuid</c> columns.
/// </summary>
public static class IntakeIdConverters
{
    /// <summary><see cref="IncomingTicketId"/> uuid converter.</summary>
    public static readonly ValueConverter<IncomingTicketId, Guid> TicketIdToUuid = new(
        static id => id.Value,
        static value => new IncomingTicketId(value));

    /// <summary><see cref="SourceConnectionId"/> uuid converter.</summary>
    public static readonly ValueConverter<SourceConnectionId, Guid> ConnectionIdToUuid = new(
        static id => id.Value,
        static value => new SourceConnectionId(value));

    /// <summary><see cref="AdmissionRuleId"/> uuid converter.</summary>
    public static readonly ValueConverter<AdmissionRuleId, Guid> RuleIdToUuid = new(
        static id => id.Value,
        static value => new AdmissionRuleId(value));

    /// <summary><see cref="ProjectId"/> uuid converter.</summary>
    public static readonly ValueConverter<ProjectId, Guid> ProjectIdToUuid = new(
        static id => id.Value,
        static value => new ProjectId(value));

    /// <summary><see cref="RunId"/> uuid converter (no cross-context FK — stored as a value).</summary>
    public static readonly ValueConverter<RunId, Guid> RunIdToUuid = new(
        static id => id.Value,
        static value => new RunId(value));
}
