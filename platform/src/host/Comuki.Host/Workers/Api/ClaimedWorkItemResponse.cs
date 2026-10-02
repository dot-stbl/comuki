using System.Text.Json.Serialization;

namespace Comuki.Host.Workers.Api;

/// <summary>
/// A claimed work item handed to the worker. Lease deadline is UTC unix
/// milliseconds on the wire (house time rule). The project id is the parent
/// run's project — the scope every project-bound worker call is confined to.
/// The proxy fields are optional: present only when the orchestrator minted
/// a per-execution virtual key at claim (proxy enabled +
/// <c>Proxy:WorkerBaseUrl</c> configured) — omitted from the JSON entirely
/// otherwise, so a claim without a mint keeps the pre-#122 wire shape. The
/// raw <see cref="VirtualKey"/> appears exactly once — here — and is never
/// journaled or logged. The source-git fields follow the same
/// exactly-once contract (harden-pi-worker-sandbox 4.3):
/// <see cref="SourceGitUrl"/> / <see cref="SourceGitRef"/> name the
/// product repository the worker clones, and <see cref="GitCredential"/>
/// is the resolved HTTPS credential from the project settings secret
/// ref — it appears exactly once, here, is never journaled or logged,
/// and the worker deletes it after the clone without ever exposing it
/// to the agent process.
/// </summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProjectId">Project the parent run belongs to.</param>
/// <param name="ProfileKey"></param>
/// <param name="EnvClass">Environment class the item runs on (task 3.2 wire contract).</param>
/// <param name="Brief"></param>
/// <param name="LeaseUntilUnixMs"></param>
/// <param name="Attempt"></param>
/// <param name="Generation">Execution generation this item was claimed under — the worker echoes this back on every later heartbeat/complete/fail call.</param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL; <c>null</c> when minting is off.</param>
/// <param name="VirtualKey">Minted proxy bearer token expiring with the lease; <c>null</c> when minting is off.</param>
/// <param name="SourceGitUrl">Product repository HTTPS URL from the project; <c>null</c> when the project has none (the worker fails the item).</param>
/// <param name="SourceGitRef">Branch or tag to clone; <c>null</c> clones the default branch.</param>
/// <param name="GitCredential">Resolved HTTPS credential for <see cref="SourceGitUrl"/>; <c>null</c> when the project has no git credential ref or the ref is unresolvable on the host.</param>
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProxyBaseUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VirtualKey = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceGitUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceGitRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GitCredential = null);
