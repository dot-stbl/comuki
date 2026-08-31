using Comuki.Engine.Orchestration.Application.Models;
using Comuki.Engine.Orchestration.Options;
using Comuki.Shared.Contracts.Queue;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Comuki.Engine.Orchestration.Application.Handlers;

/// <summary>
/// Claims one work item for a worker: validates the command, then asks the
/// queue port for the oldest queued item matching the worker's labels,
/// leasing it for <see cref="LeaseOptions.LeaseTtl"/>. A null result means
/// the queue has nothing for this worker — not an error.
/// </summary>
/// <param name="validator"></param>
/// <param name="queue"></param>
/// <param name="clock"></param>
/// <param name="leaseOptions"></param>
public sealed class ClaimWorkItemHandler(
    IValidator<ClaimWorkItemCommand> validator,
    IWorkItemQueue queue,
    TimeProvider clock,
    IOptions<LeaseOptions> leaseOptions)
{
    /// <summary>Validates and executes the claim.</summary>
    /// <param name="command"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="ValidationException">the command fails structural validation.</exception>
    public async Task<ClaimedWorkItem?> HandleAsync(ClaimWorkItemCommand command, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var now = clock.GetUtcNow();
        var leaseUntil = now.Add(leaseOptions.Value.LeaseTtl);

        return await queue.ClaimAsync(command.WorkerId, command.Labels, leaseUntil, now, cancellationToken);
    }
}
