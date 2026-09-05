using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Views;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Comuki.Modules.Intake.Application.Sources;

/// <summary>
/// CRUD service for source connections. Creation generates the webhook
/// key; the settings json keeps env-var NAMES only — the API surface
/// never accepts a secret value. The <c>secretEnvRef</c> is resolved
/// against <see cref="ISecretResolver"/> at write time so a connection
/// cannot be persisted with a credential the host cannot find.
/// </summary>
/// <param name="store"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="secrets"></param>
/// <param name="logger"></param>
public sealed class SourceConnectionService(
    IIntakeStore store,
    TimeProvider clock,
    IValidator<CreateSourceConnectionCommand> validator,
    ISecretResolver secrets,
    ILogger<SourceConnectionService> logger)
{
    /// <summary>Creates a connection and returns its view with the hook path.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="SecretEnvRefUnsetException">The named env var is not set on the host.</exception>
    public async Task<SourceConnectionView> CreateAsync(CreateSourceConnectionCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var trimmedRef = command.SecretEnvRef.Trim();
        EnsureSecretResolvable(trimmedRef);

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
    /// <exception cref="SecretEnvRefUnsetException">A new <paramref name="secretEnvRef"/> names an unset env var.</exception>
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
            EnsureSecretResolvable(nextRef.Trim());
        }

        connection.Update(name, settingsJson, secretEnvRef, enabled, clock.GetUtcNow());
        await store.UpdateConnectionAsync(connection, cancellationToken);
        logger.LogInformation("Source connection {ConnectionId} updated", connectionId);

        return SourceConnectionView.Of(connection);
    }

    /// <summary>
    /// Resolves the env-var name through the secret resolver and throws
    /// when the host does not have it set. The check fires at write time
    /// because that is when the operator can act on the answer — a saved
    /// connection with a missing secret is an unreachable source.
    /// </summary>
    /// <param name="envRef">Trimmed env-var name.</param>
    private void EnsureSecretResolvable(string envRef)
    {
        var resolved = secrets.Resolve(envRef);
        if (string.IsNullOrEmpty(resolved))
        {
            throw new SecretEnvRefUnsetException(envRef);
        }
    }

    /// <summary>Deletes a connection (idempotent).</summary>
    /// <param name="connectionId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task DeleteAsync(SourceConnectionId connectionId, CancellationToken cancellationToken = default)
    {
        return store.DeleteConnectionAsync(connectionId, cancellationToken);
    }
}
