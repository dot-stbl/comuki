using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Secrets;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// CRUD service for source connections. Creation generates the webhook
/// key; the settings json keeps env-var NAMES only — the API surface
/// never accepts a secret value. The <c>secretEnvRef</c> is resolved
/// against <see cref="ISecretResolver"/> at write time so a connection
/// cannot be persisted with a credential the host cannot find. The
/// check lives in <see cref="SecretRefResolverGuard"/> (separate class
/// per <c>code-shape.md</c> §1a — no private methods in production).
/// </summary>
/// <param name="store"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="secretGuard">Writes the typed failure when a ref resolves empty.</param>
/// <param name="logger"></param>
public sealed class SourceConnectionService(
    IIntakeStore store,
    TimeProvider clock,
    IValidator<CreateSourceConnectionCommand> validator,
    SecretRefResolverGuard secretGuard,
    ILogger<SourceConnectionService> logger)
{
    /// <summary>Creates a connection and returns its view with the hook path.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="SecretRefUnsetException">The named env var is not set on the host.</exception>
    public async Task<SourceConnectionView> CreateAsync(CreateSourceConnectionCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var trimmedRef = command.SecretEnvRef.Trim();
        await secretGuard.EnsureResolvableAsync(trimmedRef, cancellationToken);

        var connection = SourceConnection.Create(
            command.ProjectId,
            TicketProviderKeys.TryParse(command.Provider)!.Value,
            command.Name,
            command.SettingsJson.Trim(),
            trimmedRef,
            WebhookKeyGenerator.Generate(),
            clock.GetUtcNow());

        await store.AddConnectionAsync(connection, cancellationToken);
        logger.LogInformation("Source connection {ConnectionId} created for provider {Provider}", connection.Id, command.Provider);

        return SourceConnectionView.Of(connection);
    }

    /// <summary>Lists connections, optionally per project.</summary>
    /// <param name="projectId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<SourceConnectionView>> ListAsync(ProjectId? projectId, CancellationToken cancellationToken = default)
    {
        var connections = await store.ListConnectionsAsync(projectId, cancellationToken);
        return [.. connections.Select(SourceConnectionView.Of)];
    }

    /// <summary>Reads one connection.</summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="SourceConnectionNotFoundException">Unknown id.</exception>
    public async Task<SourceConnectionView> GetAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await store.FindConnectionAsync(connectionId, cancellationToken)
            ?? throw new SourceConnectionNotFoundException(connectionId);

        return SourceConnectionView.Of(connection);
    }

    /// <summary>Partial update (PATCH semantics — null fields stay).</summary>
    /// <param name="connectionId"></param>
    /// <param name="name"></param>
    /// <param name="settingsJson"></param>
    /// <param name="secretEnvRef"></param>
    /// <param name="enabled"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="SourceConnectionNotFoundException">Unknown id.</exception>
    /// <exception cref="SecretRefUnsetException">A new <paramref name="secretEnvRef"/> names an unset env var.</exception>
    public async Task<SourceConnectionView> UpdateAsync(
        SourceConnectionId connectionId,
        string? name,
        string? settingsJson,
        string? secretEnvRef,
        bool? enabled,
        CancellationToken cancellationToken = default)
    {
        var connection = await store.FindConnectionAsync(connectionId, cancellationToken)
            ?? throw new SourceConnectionNotFoundException(connectionId);

        if (secretEnvRef is { } nextRef)
        {
            await secretGuard.EnsureResolvableAsync(nextRef.Trim(), cancellationToken);
        }

        connection.Update(name, settingsJson, secretEnvRef, enabled, clock.GetUtcNow());
        await store.UpdateConnectionAsync(connection, cancellationToken);
        logger.LogInformation("Source connection {ConnectionId} updated", connectionId);

        return SourceConnectionView.Of(connection);
    }

    /// <summary>Deletes a connection (idempotent).</summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task DeleteAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        return store.DeleteConnectionAsync(connectionId, cancellationToken);
    }

    /// <summary>
    /// Generates a fresh webhook secret, persists the rotation on the row,
    /// and returns the new value for one-time disclosure (issue #46). The
    /// structured log entry uses the stable <c>source.secret_rotated</c>
    /// event id — downstream consumers (the dashboard's audit trail,
    /// billing) key off that string.
    /// </summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="SourceConnectionNotFoundException">Unknown id.</exception>
    public async Task<SecretRotationResponse> RotateSecretAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await store.FindConnectionAsync(connectionId, cancellationToken)
            ?? throw new SourceConnectionNotFoundException(connectionId);

        var newSecret = WebhookSecretGenerator.Generate();
        connection.RotateSecret(newSecret, clock.GetUtcNow());
        await store.RotateSecretAsync(connection, cancellationToken);

        // The event id is the audit key. The structured fields below it
        // never carry the secret itself — only the connection id, the
        // env-var name (which is a NAME, not a value), and the rotation
        // timestamp. The plaintext secret lives only in the response.
        logger.LogInformation(
            "source.secret_rotated {ConnectionId} {SecretEnvRef} {RotatedAt:o}",
            connection.Id,
            connection.SecretEnvRef,
            connection.UpdatedAt);

        return SecretRotationResponse.Of(connection, newSecret);
    }
}
