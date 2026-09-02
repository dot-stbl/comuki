namespace Comuki.Modules.Intake.Infrastructure.Providers.GitHub;

/// <summary>GitHub label wire shape.</summary>
public sealed record GitHubLabel
{
    /// <summary>Label name.</summary>
    public string Name { get; init; } = string.Empty;
}
