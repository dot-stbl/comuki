using Comuki.Modules.Identity.Domain.Ids;

namespace Comuki.Modules.Identity.Application.Sessions;

/// <summary>
/// Outcome of a local login: on success the caller gets the account id
/// plus the security stamp to embed in the cookie; on failure a stable
/// failure code (never a reason that would enumerate accounts — an
/// unknown user and a wrong password read identically).
/// </summary>
public sealed record LoginResult
{
    /// <summary>Failure code: wrong email or password (deliberately shared).</summary>
    public const string FailureInvalidCredentials = "invalid_credentials";

    /// <summary>Failure code: the account authenticates via OIDC only.</summary>
    public const string FailureNoPassword = "no_password";

    /// <summary>Failure code: the account is disabled.</summary>
    public const string FailureDisabled = "user_disabled";

    private LoginResult()
    {
    }

    /// <summary>Whether the credentials were accepted.</summary>
    public bool Success { get; private init; }

    /// <summary>The authenticated account.</summary>
    public UserId? UserId { get; private init; }

    /// <summary>The account's security stamp for the cookie claims.</summary>
    public int TokensVersion { get; private init; }

    /// <summary>Stable failure code when <see cref="Success"/> is false.</summary>
    public string? FailureCode { get; private init; }

    /// <summary>Builds the success outcome.</summary>
    /// <param name="userId"></param>
    /// <param name="tokensVersion"></param>
    /// <returns></returns>
    public static LoginResult Succeeded(UserId userId, int tokensVersion)
    {
        return new LoginResult { Success = true, UserId = userId, TokensVersion = tokensVersion };
    }

    /// <summary>Builds a failure outcome.</summary>
    /// <param name="failureCode"></param>
    /// <returns></returns>
    public static LoginResult Failed(string failureCode)
    {
        return new LoginResult { Success = false, FailureCode = failureCode };
    }
}
