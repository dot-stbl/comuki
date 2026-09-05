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

/// <summary>
/// Thrown when a connection's <c>SecretEnvRef</c> names an env var the
/// host does not have set. The endpoint surface answers 400 with a
/// stable <c>intake.secret_env_ref_unset</c> code so the dashboard can
/// tell the operator what to look at — a missing operator-supplied
/// secret is the operator's mistake rather than a server fault.
/// </summary>
/// <param name="EnvRef"></param>
public sealed class SecretEnvRefUnsetException(string EnvRef)
    : Exception($"environment variable '{EnvRef}' is not set on the host");
