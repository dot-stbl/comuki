using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Intake.Domain.Connections;

/// <summary>
/// The binding of one external tracker target (repo, project or queue)
/// to one Comuki project. <see cref="SettingsJson"/> carries the
/// provider-specific, non-secret settings (owner/repo, queue, site URL,
/// the env-var NAME of the outbound API token). <see cref="SecretEnvRef"/>
/// names the environment variable holding the webhook verification
/// secret — secrets themselves never live in the database.
/// <see cref="WebhookKey"/> is the generated unguessable path segment of
/// the webhook URL (<c>/api/hooks/{provider}/{key}</c>) so the platform
/// can resolve the connection before verifying the signature.
/// </summary>
public sealed class SourceConnection
{
    internal SourceConnection()
    {
    }

    /// <summary>Strong-typed connection id (UUIDv7).</summary>
    public SourceConnectionId Id { get; private set; }

    /// <summary>Project the connection feeds tickets into.</summary>
    public ProjectId ProjectId { get; private set; }

    /// <summary>Which tracker this connection talks to.</summary>
    public TicketProvider Provider { get; private set; }

    /// <summary>Human-readable name shown in the UI.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Provider-specific settings as jsonb (never secrets — env-var names only).</summary>
    public string SettingsJson { get; private set; } = string.Empty;

    /// <summary>Environment variable name holding the webhook verification secret.</summary>
    public string SecretEnvRef { get; private set; } = string.Empty;

    /// <summary>Generated webhook URL key — the per-connection routing segment.</summary>
    public string WebhookKey { get; private set; } = string.Empty;

    /// <summary>Disabled connections are skipped by the webhook and catalog flows.</summary>
    public bool Enabled { get; private set; }

    /// <summary>When the connection was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Last mutation timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a connection with a freshly generated webhook key.</summary>
    /// <param name="projectId"></param>
    /// <param name="provider"></param>
    /// <param name="name"></param>
    /// <param name="settingsJson"></param>
    /// <param name="secretEnvRef"></param>
    /// <param name="webhookKey"></param>
    /// <param name="now"></param>
    public static SourceConnection Create(
        ProjectId projectId,
        TicketProvider provider,
        string name,
        string settingsJson,
        string secretEnvRef,
        string webhookKey,
        DateTimeOffset now)
    {
        return new SourceConnection
        {
            Id = SourceConnectionId.New(),
            ProjectId = projectId,
            Provider = provider,
            Name = name.Trim(),
            SettingsJson = settingsJson,
            SecretEnvRef = secretEnvRef.Trim(),
            WebhookKey = webhookKey,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Partial update: a null field leaves the stored value untouched.</summary>
    /// <param name="name"></param>
    /// <param name="settingsJson"></param>
    /// <param name="secretEnvRef"></param>
    /// <param name="enabled"></param>
    /// <param name="now"></param>
    public void Update(string? name, string? settingsJson, string? secretEnvRef, bool? enabled, DateTimeOffset now)
    {
        if (name is { } nextName)
        {
            Name = nextName.Trim();
        }

        if (settingsJson is { } nextSettings)
        {
            SettingsJson = nextSettings;
        }

        if (secretEnvRef is { } nextSecret)
        {
            SecretEnvRef = nextSecret.Trim();
        }

        if (enabled is { } nextEnabled)
        {
            Enabled = nextEnabled;
        }

        UpdatedAt = now;
    }
}
