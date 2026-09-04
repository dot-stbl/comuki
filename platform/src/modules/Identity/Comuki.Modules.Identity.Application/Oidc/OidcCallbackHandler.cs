using Comuki.Modules.Identity.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Identity.Application.Oidc;

/// <summary>
/// Stub callback handler — Commit 3 will replace the body with the
/// full token-exchange + id_token validation + cookie sign-in. The
/// stub returns an <c>oidc.not_implemented</c> failure so the wiring
/// is exercised end-to-end while the real handler is in flight.
/// </summary>
/// <param name="stateStore"></param>
/// <param name="options"></param>
/// <param name="logger"></param>
public sealed class OidcCallbackHandler(
    IOidcStateStore stateStore,
    IOptions<OidcOptions> options,
    ILogger<OidcCallbackHandler> logger)
{
    /// <summary>Stable failure code for an unimplemented handler.</summary>
    public const string NotImplementedCode = "oidc.callback_not_implemented";

    /// <summary>
    /// Stub implementation: validates that a state token was supplied,
    /// returns an <c>oidc.callback_not_implemented</c> failure otherwise
    /// the wiring would 200 with an empty redirect.
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    public Task<OidcCallbackResult> HandleAsync(OidcCallbackRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (!string.IsNullOrWhiteSpace(request.Error))
        {
            logger.LogInformation("Oidc callback surfaced provider error {Error}: {Description}", request.Error, request.ErrorDescription);

            return Task.FromResult(new OidcCallbackResult(
                Success: false,
                RedirectTarget: "/login",
                FailureCode: $"oidc.provider_{request.Error}"));
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return Task.FromResult(new OidcCallbackResult(false, "/login", "oidc.state_missing"));
        }

        if (!Guid.TryParse(request.State, out _))
        {
            return Task.FromResult(new OidcCallbackResult(false, "/login", "oidc.state_malformed"));
        }

        _ = stateStore;
        _ = options;

        return Task.FromResult(new OidcCallbackResult(false, "/login", NotImplementedCode));
    }
}
