namespace Comuki.Modules.Intake.Domain.Tickets;

/// <summary>
/// The closed set of ticket sources the platform speaks: four external
/// trackers plus the native API surface. Stored as the kebab-case key
/// (see <see cref="TicketProviderKeys"/>) so the webhook route segment,
/// the delivery rows and the sync jobs all share one wire form.
/// </summary>
public enum TicketProvider
{
    /// <summary>GitHub Issues (webhooks + REST v3).</summary>
    GitHub,

    /// <summary>GitLab Issues (webhooks + REST v4).</summary>
    GitLab,

    /// <summary>Yandex Tracker (webhooks + issues search API).</summary>
    YandexTracker,

    /// <summary>Jira (webhooks + REST v2).</summary>
    Jira,

    /// <summary>The native <c>POST /api/v1/tickets</c> surface — no external tracker.</summary>
    Native,
}

/// <summary>
/// Kebab-case source keys of <see cref="TicketProvider"/> — the single
/// source for webhook route segments and provider lookup; matches the
/// <c>SubjectTypeKeys</c> pattern of the Identity module.
/// </summary>
public static class TicketProviderKeys
{
    /// <summary>Key of <see cref="TicketProvider.GitHub"/>.</summary>
    public const string GitHub = "github";

    /// <summary>Key of <see cref="TicketProvider.GitLab"/>.</summary>
    public const string GitLab = "gitlab";

    /// <summary>Key of <see cref="TicketProvider.YandexTracker"/>.</summary>
    public const string YandexTracker = "yandex-tracker";

    /// <summary>Key of <see cref="TicketProvider.Jira"/>.</summary>
    public const string Jira = "jira";

    /// <summary>Key of <see cref="TicketProvider.Native"/>.</summary>
    public const string Native = "native";

    /// <summary>Every known key, in declaration order.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>([GitHub, GitLab, YandexTracker, Jira, Native], StringComparer.Ordinal);

    /// <summary>The kebab-case key of a provider.</summary>
    /// <param name="provider"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException">Unknown provider.</exception>
    public static string Key(TicketProvider provider)
    {
        return provider switch
        {
            TicketProvider.GitHub => GitHub,
            TicketProvider.GitLab => GitLab,
            TicketProvider.YandexTracker => YandexTracker,
            TicketProvider.Jira => Jira,
            TicketProvider.Native => Native,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    /// <summary>Parses a key back to the provider; unknown keys answer null.</summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static TicketProvider? TryParse(string? key)
    {
        return key switch
        {
            GitHub => TicketProvider.GitHub,
            GitLab => TicketProvider.GitLab,
            YandexTracker => TicketProvider.YandexTracker,
            Jira => TicketProvider.Jira,
            Native => TicketProvider.Native,
            _ => null,
        };
    }
}
