using Comuki.Engine.Compute.Security;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Workers;

/// <summary>
/// Maps a presented bearer token back to the <see cref="WorkerId"/> it was
/// issued for. Shared by the worker REST endpoints and the gRPC service;
/// misses are values (null), callers decide how to reject.
/// </summary>
/// <param name="issuer"></param>
public sealed class WorkerTokenAuthenticator(WorkerTokenIssuer issuer)
{
    /// <summary>Validates the presented token (with or without a Bearer prefix).</summary>
    /// <param name="presentedToken"></param>
    public WorkerId? Authenticate(string? presentedToken)
    {
        return presentedToken is null
            ? null
            : issuer.Validate(WorkerTokenHeaders.StripBearerPrefix(presentedToken));
    }
}

/// <summary>Header-name constants and tolerant bearer-token extraction.</summary>
internal static class WorkerTokenHeaders
{
    /// <summary>HTTP header carrying the worker token (REST claim/heartbeat endpoints).</summary>
    public const string HttpAuthorizationHeader = "Authorization";

    /// <summary>gRPC metadata key carrying the worker token (lowercase, per gRPC convention).</summary>
    public const string GrpcAuthorizationKey = "authorization";

    private const string BearerPrefix = "Bearer ";

    public static string? TryGetFromHttp(IHeaderDictionary headers)
    {
        return headers.TryGetValue(HttpAuthorizationHeader, out var value) && value.Count > 0
            ? value[0]
            : null;
    }

    public static string? TryGetFromGrpc(global::Grpc.Core.Metadata? headers)
    {
        return headers?.GetValue(GrpcAuthorizationKey);
    }

    public static string StripBearerPrefix(string token)
    {
        return token.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? token[BearerPrefix.Length..]
            : token;
    }
}
