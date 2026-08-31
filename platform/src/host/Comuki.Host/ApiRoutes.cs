namespace Comuki.Host;

/// <summary>Host route templates - the single source for endpoint mapping; no route literals in Map* calls.</summary>
public static class ApiRoutes
{
    public const string Health = "/health";

    public const string Profiles = "/profiles";

    public const string ProfileByKey = "/profiles/{key}";

    public const string ChatCommands = "/chat-commands";

    /// <summary>API root prefix of the versioned auth surface.</summary>
    public const string AuthRoot = "api/v1/auth";

    /// <summary>Base of the per-provider OIDC routes (start endpoint and the handler-owned callback path).</summary>
    public const string AuthOidcRoot = "api/v1/auth/oidc";

    public const string Projects = "/api/v1/projects";
}
