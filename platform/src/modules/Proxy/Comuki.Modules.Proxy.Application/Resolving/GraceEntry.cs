using Comuki.Modules.Proxy.Application.Models;

namespace Comuki.Modules.Proxy.Application.Resolving;

/// <summary>
/// A recently-deleted virtual key kept answerable for the deletion grace
/// window (Q31) — the key plus the UTC instant the grace expires.
/// </summary>
/// <param name="Key">The removed key, still returned by lookups until the expiry passes.</param>
/// <param name="ExpiresAt">UTC instant the grace window closes; lookups miss after it.</param>
internal sealed record GraceEntry(VirtualKey Key, DateTimeOffset ExpiresAt);
