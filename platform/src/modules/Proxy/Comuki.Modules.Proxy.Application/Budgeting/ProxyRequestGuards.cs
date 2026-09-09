using Comuki.Modules.Proxy.Application.Models;
using Comuki.Modules.Proxy.Application.Ports;

namespace Comuki.Modules.Proxy.Application.Budgeting;

/// <summary>
/// Per-request guards for the proxy: monthly budget (Q18 — denied with
/// 402 Payment Required) and per-call input/output token caps (Q19 —
/// denied with 400 Bad Request). Returns a <see cref="GuardRejection"/>
/// when a guard fails; the caller short-circuits the YARP request
/// transform and surfaces the rejection to the client. Pure helpers —
/// no DI, no I/O beyond the primitives the caller already holds.
/// Extracted to keep <c>ProxyTransforms</c> free of inline guard logic
/// (<c>class-layout-and-tooling.md</c> §1a) and to keep the Application
/// assembly free of an <c>Microsoft.AspNetCore.App</c> dependency.
/// </summary>
public static class ProxyRequestGuards
{
    /// <summary>
    /// One rejection row: HTTP status + RFC 9457 <c>code</c> + human
    /// <c>detail</c>. <c>internal</c> because the helper is consumed
    /// from a sibling file in the same assembly.
    /// </summary>
    /// <param name="StatusCode">HTTP status the response carries.</param>
    /// <param name="Code">Stable dot.case identifier — clients branch on this.</param>
    /// <param name="Detail">Safe human detail — no PII, no secrets.</param>
    public sealed record GuardRejection(int StatusCode, string Code, string Detail);

    /// <summary>Stable code for the monthly-budget denial (Q18).</summary>
    public const string BudgetExceededCode = "proxy.budget_exceeded";

    /// <summary>Stable code for the per-call input-token denial (Q19).</summary>
    public const string InputTokensExceededCode = "proxy.input_tokens_exceeded";

    /// <summary>Stable code for the per-call output-token denial (Q19).</summary>
    public const string OutputTokensExceededCode = "proxy.output_tokens_exceeded";

    /// <summary>HTTP 402 Payment Required — used for the budget-exceeded denial.</summary>
    private const int StatusPaymentRequired = 402;

    /// <summary>HTTP 400 Bad Request — used for the per-call token-cap denials.</summary>
    private const int StatusBadRequest = 400;

    /// <summary>
    /// Walks every per-request guard in order — budget first (the
    /// cheapest rejection and the one callers most often hit), then
    /// input cap, then output cap. Returns the first failure or
    /// <c>null</c> when every guard accepts the request. Body-shape
    /// primitives (<paramref name="contentLength"/>,
    /// <paramref name="requestedMaxOutputTokens"/>) are passed by the
    /// caller so the Application assembly stays ASP.NET-Core-free.
    /// </summary>
    /// <param name="key">Resolved virtual key the request claims on.</param>
    /// <param name="enforcer">Budget enforcer for the project + month.</param>
    /// <param name="contentLength">Inbound request <c>Content-Length</c>; <c>0</c> when unknown.</param>
    /// <param name="requestedMaxOutputTokens">Parsed <c>max_tokens</c> / <c>max_output_tokens</c> from the body; <c>null</c> when the body is absent or unparsable.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<GuardRejection?> EvaluateAsync(
        VirtualKey key,
        IProxyBudgetEnforcer enforcer,
        long contentLength,
        int? requestedMaxOutputTokens,
        CancellationToken cancellationToken = default)
    {
        var verdict = await enforcer.EvaluateAsync(key, cancellationToken);
        if (!verdict.Allowed)
        {
            return new GuardRejection(
                StatusPaymentRequired,
                BudgetExceededCode,
                "virtual key monthly budget exceeded; payment required");
        }

        if (key.MaxInputTokens is { } maxInputTokens)
        {
            var estimated = ProxyBudgetMath.EstimateInputTokens(contentLength);
            if (estimated > maxInputTokens)
            {
                return new GuardRejection(
                    StatusBadRequest,
                    InputTokensExceededCode,
                    $"request exceeds the configured MaxInputTokens cap of {maxInputTokens}");
            }
        }

        return key.MaxOutputTokens is { } maxOutputTokens
            && requestedMaxOutputTokens is { } requested
            && requested > maxOutputTokens
            ? new GuardRejection(
                StatusBadRequest,
                OutputTokensExceededCode,
                $"request exceeds the configured MaxOutputTokens cap of {maxOutputTokens}")
            : null;
    }
}
