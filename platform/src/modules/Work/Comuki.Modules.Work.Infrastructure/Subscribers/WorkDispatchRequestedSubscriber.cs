using Comuki.Modules.Work.Application.Events;
using Comuki.Modules.Work.Application.Ports.Engine;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Infrastructure.Inbox;
using Comuki.Modules.Work.Infrastructure.Options;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Contracts.Work;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Work.Infrastructure.Subscribers;

/// <summary>
/// Host-composed <see cref="WorkSubscriberBase"/> that polls
/// <c>orchestration.outbox_messages</c> for the
/// <c>work.task.attempt-requested.v1</c> event the Work side
/// publishes from <c>DispatchRunHandler</c> (task 2.3). The worker
/// is the single owner of the dispatch-watermark for this type
/// (<see cref="IWorkInboxWatermarkStore"/>)
/// so a host restart resumes from the last-seen id and never
/// re-processes a historical row. Per-cycle work: for each new
/// row, deserialise the <see cref="WorkTaskEvent"/> payload,
/// load the matching <see cref="Domain.WorkTask"/> from the
/// store to recover the brief / inbound id, and call
/// <c>IRunLauncher.LaunchAsync(ProjectId, WorkDispatchItem, ct)</c>
/// (the Work-side overload — host-composed by
/// <c>IntegrationRunLauncher</c>, task 2.3a / 5.x). The
/// dispatch-watermark lives under a distinct key from the
/// engine-terminal subscription (the two streams share
/// schema, not dedupe ledger).
/// <para>
/// <b>Dispatch envelope shape.</b> The bridge reuses the launcher
/// inbox-claim contract for replays (the message id
/// <c>work.task.{taskId}:dispatch:{ordinal}</c> deduplicates on
/// the engine side), so the
/// <see cref="WorkDispatchItem"/> we build here is the
/// <i>first</i> shot. A repeated poll lands on the same claim
/// and the launcher returns the winner's <see cref="RunId"/>.
/// The Task-side <see cref="Domain.WorkTask.AppendAttempt"/>
/// row is the mirror — the launcher's returned Run id is
/// stamped on the WorkTask when its attempt row is terminal.
/// </para>
/// </summary>
public sealed class WorkDispatchRequestedSubscriber(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<WorkOptions> workOptions,
    ILogger<WorkDispatchRequestedSubscriber> logger)
    : WorkSubscriberBase(scopeFactory, clock, logger)
{
    /// <inheritdoc />
    public override string Name => "work-dispatch-requested-subscriber";

    /// <inheritdoc />
    protected override IReadOnlyCollection<string> GetSubscribedTypes()
    {
        return [SubscribedType];
    }

    /// <inheritdoc />
    protected override string WatermarkKey => SubscribedType;

    /// <inheritdoc />
    public override async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        return !workOptions.Value.DispatchEnabled
            ? WorkerResult.Ok("dispatch-disabled")
            : await base.ExecuteAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<bool> HandleRowAsync(
        OrchestrationOutboxRow row,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var payload = TryReadDispatch(row.Payload);
        if (payload is null)
        {
            return false;
        }

        var store = serviceProvider.GetRequiredService<IWorkTaskStore>();
        var runLauncher = serviceProvider.GetRequiredService<IWorkRunLauncher>();

        var taskId = new WorkTaskId(payload.TaskId);
        var projectId = new ProjectId(payload.ProjectId);

        // Load the Task to recover the brief and inbound id — the
        // wire envelope (WorkTaskEvent) carries neither; the
        // launcher writes both into the worker brief jsonb column.
        var task = await store.FindAsync(taskId, cancellationToken);
        if (task is null)
        {
            // The Task was deleted between the dispatch publish
            // and our poll — the engine-side inbox claim is the
            // race-resolution point, not here. The watermark
            // still advances so the cycle doesn't loop.
            Logger.LogDebug(
                "WorkDispatchRequestedSubscriber skipping dispatch row {RowId}: task {TaskId} not found",
                row.Id, taskId);
            return false;
        }

        var inboundItemExternalId = task.SourceRefs
            .Where(static source => source.IsPrimary)
            .Select(static source => source.ExternalId)
            .FirstOrDefault();

        // Dispatch-policy defaults come from WorkOptions (config-bound
        // by the host). The string literals still include the design
        // §Decision 2 placeholders as init defaults — the operator
        // can override per-environment through the TOML section
        // once work-management Phase B lands.
        var policy = workOptions.Value;
        var dispatchItem = new WorkDispatchItem(
            TaskId: payload.TaskId.ToString(),
            ProjectId: payload.ProjectId.ToString(),
            AttemptOrdinal: payload.AttemptOrdinal ?? 1,
            ProfileKey: policy.DefaultProfileKey,
            Image: policy.DefaultWorkerImage,
            EnvClass: policy.DefaultEnvClass,
            ProfilesRef: policy.DefaultProfilesRef,
            Brief: task.Brief,
            InboundItemExternalId: inboundItemExternalId);

        // The launcher's inbox-claim key (work.task.{taskId}:dispatch:{attemptOrdinal})
        // handles replays natively — a re-poll of the same outbox row
        // returns the winner's RunId.
        var runId = await runLauncher.LaunchAsync(projectId, dispatchItem, cancellationToken);

        // Stamp the WorkTask's activeAttemptId with the engine's
        // RunId. The aggregate's AppendAttempt was already called
        // by DispatchRunHandler on the Work side (the journal row
        // carries the same RunId); here we mirror it back so a Task
        // read sees the engine-side Run.
        if (task.ActiveAttemptId is null)
        {
            task.AppendAttempt(runId, Clock.GetUtcNow());
            await store.SaveAsync(task, cancellationToken);
        }

        return true;
    }

    /// <summary>The single Work-dispatch event type the subscriber polls.</summary>
    private const string SubscribedType = WorkTaskEventTypes.AttemptRequested;

    /// <summary>Best-effort parse of the dispatch event payload — null on malformed.</summary>
    private static WorkTaskEvent? TryReadDispatch(string payload)
    {
        return WorkTaskEventJson.TryDeserialize<WorkTaskEvent>(payload);
    }

}
