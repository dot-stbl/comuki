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
/// journaled or logged.
/// </summary>
/// <param name="WorkItemId"></param>
/// <param name="RunId"></param>
/// <param name="ProjectId">Project the parent run belongs to.</param>
/// <param name="ProfileKey"></param>
/// <param name="Brief"></param>
/// <param name="LeaseUntilUnixMs"></param>
/// <param name="Attempt"></param>
/// <param name="ProxyBaseUrl">Worker-facing proxy base URL; <c>null</c> when minting is off.</param>
/// <param name="VirtualKey">Minted proxy bearer token expiring with the lease; <c>null</c> when minting is off.</param>
public sealed record ClaimedWorkItemResponse(
    Guid WorkItemId,
    Guid RunId,
    Guid ProjectId,
    string ProfileKey,
    string Brief,
    long LeaseUntilUnixMs,
    int Attempt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProxyBaseUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VirtualKey = null);
