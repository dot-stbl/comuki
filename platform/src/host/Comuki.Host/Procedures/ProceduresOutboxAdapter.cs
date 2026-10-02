using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Outbox;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Contracts;

namespace Comuki.Host.Procedures;

/// <summary>
/// Host-side bridge from the cross-module <see cref="IOutbox"/> seam to
/// the engine's durable outbox: serializes the payload and stages an
/// <see cref="OutboxMessage"/> on the orchestration context, so the
/// platform's existing outbox dispatcher delivers it. Singleton —
/// reaches the scoped <see cref="OrchestrationDbContext"/> through a
/// fresh scope per publish, never capturing one.
/// </summary>
/// <param name="scopeFactory">Creates the scope holding the orchestration context.</param>
/// <param name="clock">Time source for the staged message's stamp.</param>
internal sealed class ProceduresOutboxAdapter(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock) : IOutbox
{
    /// <inheritdoc />
    public async Task PublishAsync(
        string eventType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        dbContext.Add(OutboxMessage.Create(
            eventType,
            JsonSerializer.Serialize(payload, JsonSerializerOptions.Web),
            clock.GetUtcNow()));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
