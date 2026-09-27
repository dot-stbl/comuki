using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>Thrown when a source connection id is unknown (404).</summary>
/// <param name="ConnectionId"></param>
public sealed class SourceConnectionNotFoundException(SourceConnectionId ConnectionId)
    : DomainException(ErrorCode, $"source connection '{ConnectionId}' not found")
{
    private const string ErrorCode = "intake.connection_not_found";
}

/// <summary>Thrown when an admission rule id is unknown (404).</summary>
/// <param name="RuleId"></param>
public sealed class AdmissionRuleNotFoundException(AdmissionRuleId RuleId)
    : DomainException(ErrorCode, $"admission rule '{RuleId}' not found")
{
    private const string ErrorCode = "intake.rule_not_found";
}
