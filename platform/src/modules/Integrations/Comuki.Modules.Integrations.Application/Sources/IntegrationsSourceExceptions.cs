using Comuki.Modules.Integrations.Domain.Ids;
using Comuki.Shared.Kernel.Exceptions;

namespace Comuki.Modules.Integrations.Application.Sources;

/// <summary>Thrown when a source connection id is unknown (404).</summary>
public sealed class SourceConnectionNotFoundException(SourceConnectionId ConnectionId)
    : DomainException(ErrorCode, $"source connection '{ConnectionId}' not found")
{
    private const string ErrorCode = "integration.connection_not_found";
}

/// <summary>Thrown when an admission rule id is unknown (404).</summary>
public sealed class AdmissionRuleNotFoundException(AdmissionRuleId RuleId)
    : DomainException(ErrorCode, $"admission rule '{RuleId}' not found")
{
    private const string ErrorCode = "integration.rule_not_found";
}
