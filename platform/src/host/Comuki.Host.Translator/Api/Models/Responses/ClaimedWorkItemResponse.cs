namespace Comuki.Host.Translator.Api.Models.Responses;

/// <summary>A claimed work item. Lease deadline is UTC unix milliseconds on the wire.</summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProjectId">Project the parent run belongs to — the worker's project context.</param>
/// <param name="ProfileKey"></param>
/// <param name="EnvClass">Environment class the item runs on (task 3.2 wire contract).</param>
/// <param name="Brief"></param>
/// <param name="LeaseUntilUnixMs"></param>
/// <param name="Attempt"></param>
/// <param name="Generation">Execution generation this item was claimed under — the worker echoes this back on every later heartbeat/complete/fail call.</param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL; <c>null</c> when the orchestrator mints no key.</param>
/// <param name="VirtualKey">Minted proxy bearer token expiring with the lease; <c>null</c> when the orchestrator mints no key.</param>
/// <param name="SourceGitUrl">Product repository HTTPS URL (harden-pi-worker-sandbox 4.3); <c>null</c> when the project has none — the item fails with <c>source.missing</c>.</param>
/// <param name="SourceGitRef">Branch or tag to clone; <c>null</c> clones the default branch.</param>
/// <param name="GitCredential">Resolved HTTPS credential for the clone; used only for the clone process and deleted afterwards — never passed to the agent environment.</param>
public sealed record ClaimedWorkItemResponse(
    Guid WorkItemId,
    Guid RunId,
    Guid ProjectId,
    string ProfileKey,
    string EnvClass,
    string Brief,
    long LeaseUntilUnixMs,
    int Attempt,
    int Generation,
    string? ProxyBaseUrl = null,
    string? VirtualKey = null,
    string? SourceGitUrl = null,
    string? SourceGitRef = null,
    string? GitCredential = null);
