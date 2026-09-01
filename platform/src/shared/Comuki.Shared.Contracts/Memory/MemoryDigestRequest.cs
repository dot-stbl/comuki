namespace Comuki.Shared.Contracts.Memory;

/// <summary>
/// Request for the shared digest service: what task is about to be fed to the
/// brain and in which memory scope. Scope mirrors the memory_facts model
/// (user / project / global) — the digest service decides how to combine.
/// </summary>
/// <param name="ScopeKind">Memory scope: <c>user</c>, <c>project</c> or <c>global</c>.</param>
/// <param name="SubjectId">Who/what the scope belongs to (user id, project id; empty for global).</param>
/// <param name="Task">The task the digest is assembled for.</param>
public sealed record MemoryDigestRequest(string ScopeKind, Guid SubjectId, string Task);
