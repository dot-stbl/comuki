namespace Comuki.Host.Auth;

/// <summary>
/// Bootstrap-admin credentials: create-and-grant once at startup so a
/// fresh deployment has a first operator. Config section wins; the
/// <c>COMUKI_BOOTSTRAP_ADMIN_*</c> env vars are the deployment-facing
/// fallback. Unset means no bootstrap; a half-set pair is refused
/// loudly rather than silently ignored.
/// </summary>
public sealed class BootstrapAdminOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "auth:bootstrap";

    /// <summary>Env var holding the bootstrap admin email.</summary>
    public const string EmailEnvVariable = "COMUKI_BOOTSTRAP_ADMIN_EMAIL";

    /// <summary>Env var holding the bootstrap admin password.</summary>
    public const string PasswordEnvVariable = "COMUKI_BOOTSTRAP_ADMIN_PASSWORD";

    /// <summary>Email of the account to bootstrap; null disables the bootstrap.</summary>
    public string? AdminEmail { get; init; }

    /// <summary>Password of the account to bootstrap; null disables the bootstrap.</summary>
    public string? AdminPassword { get; init; }

    /// <summary>
    /// Resolves the effective options: config values first, env vars
    /// filling the gaps. Throws when only one credential half is set.
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException">Exactly one of email/password is set.</exception>
    public static BootstrapAdminOptions Resolve(IConfiguration configuration)
    {
        var bound = configuration.GetSection(SectionName).Get<BootstrapAdminOptions>() ?? new BootstrapAdminOptions();
        var email = bound.AdminEmail ?? Environment.GetEnvironmentVariable(EmailEnvVariable);
        var password = bound.AdminPassword ?? Environment.GetEnvironmentVariable(PasswordEnvVariable);

        return email is null != password is null
            ? throw new InvalidOperationException(
                $"bootstrap admin is half-configured: both {SectionName}:adminEmail/adminPassword (or env "
                + $"{EmailEnvVariable}/{PasswordEnvVariable}) must be set, or neither")
            : new BootstrapAdminOptions { AdminEmail = email, AdminPassword = password };
    }
}
