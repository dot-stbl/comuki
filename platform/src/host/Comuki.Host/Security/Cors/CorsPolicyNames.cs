namespace Comuki.Host.Security.Cors;

/// <summary>Named CORS policies registered by <see cref="ComukiCorsInstaller"/>.</summary>
public static class CorsPolicyNames
{
    /// <summary>Dashboard SPA + API-key cross-origin callers.</summary>
    public const string Dashboard = "comuki.dashboard";
}
