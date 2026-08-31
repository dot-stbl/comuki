using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Compute.Security;

/// <summary>
/// Issues and validates short-lived opaque worker tokens. Only the
/// HMAC-SHA256(pepper, token) is stored; validation scans every record with a
/// fixed-time comparison so timing does not reveal which token matched.
/// </summary>
// MA0038/CA1822 suppressed: the Roslynator 4.15 "make static" fixer mis-handles
// primary-constructor captures — it marks methods static while they still capture
// ctor parameters (clock/store/tokenOptions), which breaks compilation (CS9105).
#pragma warning disable MA0038 // Make method static (do not use with primary ctor captures)
public sealed class WorkerTokenIssuer(
    TimeProvider clock,
    IWorkerTokenStore store,
    IOptions<WorkerTokenOptions> tokenOptions)
{
    private const int TokenBytes = 32;

    /// <summary>
    /// Issues a fresh 256-bit opaque token (base64url) for the worker and stores
    /// its hash with the expiry. Replaces any previous token of the worker.
    /// </summary>
    /// <param name="workerId"></param>
    /// <param name="timeToLive">Overrides <see cref="WorkerTokenOptions.TokenTtl"/> when set.</param>
    public string Issue(WorkerId workerId, TimeSpan? timeToLive = null)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = clock.GetUtcNow().Add(timeToLive ?? tokenOptions.Value.TokenTtl);
        store.Save(new WorkerTokenRecord(WorkerTokenHasher.Hash(token, tokenOptions.Value.Pepper), workerId, expiresAt));
        return token;
    }

    /// <summary>
    /// Validates a presented token. Returns the worker it belongs to, or null
    /// when the token is unknown, expired, or revoked.
    /// </summary>
    /// <param name="token"></param>
    public WorkerId? Validate(string token)
    {
        var hash = WorkerTokenHasher.Hash(token, tokenOptions.Value.Pepper);
        var now = clock.GetUtcNow();
        WorkerId? matchedWorkerId = null;
        foreach (var record in store.List())
        {
            // no early exit: timing must not depend on which record matched
            if (record.ExpiresAt > now && WorkerTokenHasher.FixedTimeEquals(record.TokenHash, hash))
            {
                matchedWorkerId = record.WorkerId;
            }
        }

        return matchedWorkerId;
    }

    /// <summary>Revokes the token of a worker (stop / lease-expire path).</summary>
    /// <param name="workerId"></param>
    public void Revoke(WorkerId workerId) => store.Revoke(workerId);
}
#pragma warning restore MA0038 // Make method static (do not use with primary ctor captures)

/// <summary>HMAC hashing and fixed-time comparison for worker tokens.</summary>
file static class WorkerTokenHasher
{
    public static string Hash(string token, string pepper)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        return Base64Url.EncodeToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }

    public static bool FixedTimeEquals(string storedHash, string computedHash)
    {
        return CryptographicOperations.FixedTimeEquals(
            Base64Url.DecodeFromChars(storedHash.AsSpan()),
            Base64Url.DecodeFromChars(computedHash.AsSpan()));
    }
}
