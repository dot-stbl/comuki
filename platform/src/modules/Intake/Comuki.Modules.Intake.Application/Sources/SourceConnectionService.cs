using Comuki.Modules.Intake.Application.Ports;
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
/// never accepts a secret value.
/// </summary>
/// <param name="store"></param>
/// <param name="clock"></param>
/// <param name="validator"></param>
/// <param name="logger"></param>
public sealed class SourceConnectionService(
    IIntakeStore store,
    TimeProvider clock,
    IValidator<CreateSourceConnectionCommand> validator,
    ILogger<SourceConnectionService> logger)
{
    /// <summary>Creates a connection and returns its view with the hook path.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SourceConnectionView> CreateAsync(CreateSourceConnectionCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var connection = SourceConnection.Create(
            command.ProjectId,
            TicketProviderKeys.TryParse(command.Provider)!.Value,
            command.Name,
            command.SettingsJson.Trim(),
            command.SecretEnvRef.Trim(),
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
}
