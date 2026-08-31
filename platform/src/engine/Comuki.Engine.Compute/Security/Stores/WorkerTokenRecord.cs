using Comuki.Shared.Kernel.Ids;

namespace Comuki.Engine.Compute.Security.Stores;

/// <summary>
/// A stored worker token: HMAC-SHA256 of the opaque token (base64url) plus the
/// worker it belongs to and its expiry. The plaintext token is never stored.
/// </summary>
/// <param name="TokenHash">HMAC-SHA256(pepper, token), base64url.</param>
/// <param name="WorkerId"></param>
/// <param name="ExpiresAt"></param>
public sealed record WorkerTokenRecord(string TokenHash, WorkerId WorkerId, DateTimeOffset ExpiresAt);
