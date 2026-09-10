using Comuki.Modules.Intake.Domain.Ids;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>Thrown when a source connection id is unknown (404).</summary>
/// <param name="ConnectionId"></param>
public sealed class SourceConnectionNotFoundException(SourceConnectionId ConnectionId)
    : Exception($"source connection '{ConnectionId}' not found");

/// <summary>Thrown when an admission rule id is unknown (404).</summary>
/// <param name="RuleId"></param>
public sealed class AdmissionRuleNotFoundException(AdmissionRuleId RuleId)
    : Exception($"admission rule '{RuleId}' not found");
