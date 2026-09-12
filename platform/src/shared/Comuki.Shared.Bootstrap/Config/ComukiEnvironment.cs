namespace Comuki.Shared.Bootstrap.Config;

/// <summary>
/// Environment resolution with a Comuki-native face (issue #54):
/// <c>COMUKI_ENV</c> is the primary variable, <c>ASPNETCORE_ENVIRONMENT</c>
/// and <c>DOTNET_ENVIRONMENT</c> remain quiet fallbacks so stock .NET
/// tooling keeps working. Nothing set resolves to <c>Production</c>.
/// </summary>
public static class ComukiEnvironment
{
    /// <summary>Primary env var: development | production.</summary>
    public const string EnvironmentVariable = "COMUKI_ENV";

    /// <summary>First quiet fallback (ASP.NET Core tooling).</summary>
    public const string AspNetCoreFallbackVariable = "ASPNETCORE_ENVIRONMENT";

    /// <summary>Second quiet fallback (generic host tooling).</summary>
    public const string DotnetFallbackVariable = "DOTNET_ENVIRONMENT";

    /// <summary>The environment used when nothing is set.</summary>
    public const string ProductionName = "Production";

    private static readonly string[] candidateVariables =
    [
        EnvironmentVariable,
        AspNetCoreFallbackVariable,
        DotnetFallbackVariable,
    ];

    /// <summary>
    /// Outcome of an environment resolution: the effective value plus the
    /// env var that supplied it (null = the Production default). Consumed
    /// by <c>comuki doctor</c> (issue #56) to surface deprecated fallback
    /// usage.
    /// </summary>
    /// <param name="Environment">The effective environment name.</param>
    /// <param name="Variable">The env var that supplied it, or null for the default.</param>
    public sealed record Resolution(string Environment, string? Variable)
    {
        /// <summary>True when the value came from the primary COMUKI_ENV variable or from the default.</summary>
        public bool FromPrimary => Variable is null || string.Equals(Variable, EnvironmentVariable, StringComparison.Ordinal);
    }

    /// <summary>Resolves the effective environment from the process env.</summary>
    public static string Resolve()
    {
        return ResolveDetailed(Environment.GetEnvironmentVariable).Environment;
    }

    /// <summary>Resolution core, parameterised by an env lookup for tests.</summary>
    /// <param name="lookup">Env-var accessor (name → value or null).</param>
    /// <returns>The first non-blank value among COMUKI_ENV → ASPNETCORE_ENVIRONMENT → DOTNET_ENVIRONMENT, else Production.</returns>
    public static string Resolve(Func<string, string?> lookup)
    {
        return ResolveDetailed(lookup).Environment;
    }

    /// <summary>Resolves the effective environment with its provenance, from the process env.</summary>
    public static Resolution ResolveDetailed()
    {
        return ResolveDetailed(Environment.GetEnvironmentVariable);
    }

    /// <summary>Detailed resolution core, parameterised by an env lookup for tests and tooling.</summary>
    /// <param name="lookup">Env-var accessor (name → value or null).</param>
    /// <returns>The resolved value and the variable that supplied it.</returns>
    public static Resolution ResolveDetailed(Func<string, string?> lookup)
    {
        foreach (var variable in candidateVariables)
        {
            var value = lookup(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new Resolution(value.Trim(), variable);
            }
        }

        return new Resolution(ProductionName, null);
    }
}
