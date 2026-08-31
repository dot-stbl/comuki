using Microsoft.AspNetCore.Authentication;

namespace Comuki.Modules.Identity.Infrastructure.Security.ApiKeys;

/// <summary>Options of the API-key scheme — only the header name is configurable.</summary>
public sealed class ApiKeySchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>The header the token arrives in.</summary>
    public string HeaderName { get; set; } = "Authorization";
}
