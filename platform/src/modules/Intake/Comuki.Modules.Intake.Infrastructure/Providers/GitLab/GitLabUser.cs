namespace Comuki.Modules.Intake.Infrastructure.Providers.GitLab;

/// <summary>GitLab user wire shape.</summary>
public sealed record GitLabUser
{
    /// <summary>Username.</summary>
    public string Username { get; init; } = string.Empty;
}
