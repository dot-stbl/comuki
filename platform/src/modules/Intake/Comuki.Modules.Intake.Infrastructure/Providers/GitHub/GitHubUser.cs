namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>GitHub user wire shape.</summary>
public sealed record GitHubUser
{
    /// <summary>User login.</summary>
    public string Login { get; init; } = string.Empty;
}
