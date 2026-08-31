using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.ApiKeys;

/// <summary>
/// The result of an issue operation. The plaintext token exists exactly
/// here — it is never persisted, never logged, and shown to the caller
/// once. Everything else (prefix, hmac) is what the database keeps.
/// </summary>
/// <param name="Id"></param>
/// <param name="Name"></param>
/// <param name="Prefix"></param>
/// <param name="PlaintextToken">The full <c>ck_…</c> token — shown once.</param>
public sealed record IssuedApiKeyCredential(ApiKeyId Id, string Name, string Prefix, string PlaintextToken);
