using Comuki.Modules.Work.Application.Admission;
using Comuki.Modules.Work.Application.Cancellation;
using Comuki.Modules.Work.Application.Dispatch;
using Comuki.Modules.Work.Application.Ports.Persistence;
using Comuki.Modules.Work.Domain;
using Comuki.Modules.Work.Domain.Ids;
using Comuki.Modules.Work.Unit.Fakes;
using Comuki.Shared.Contracts;
using Comuki.Shared.Contracts.Work;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Comuki.Modules.Work.Unit;

/// <summary>
/// In-memory <see cref="IWorkTaskStore"/> for the
/// Application-handler T0 tests. Keeps the loaded Task in a
/// dictionary; a null <c>FindAsync</c> after a <c>SaveAsync</c>
/// means the Task was deleted (not exercised in the current
/// workstream). The store is a write-through cache — it does
/// not enforce any EF-level optimistic concurrency in unit
/// tests; the Work.Infrastructure EF implementation will.
/// </summary>
public sealed class InMemoryWorkTaskStore : IWorkTaskStore
{
    private readonly Dictionary<WorkTaskId, WorkTask> byId = [];

    public Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken = default)
    {
        byId.TryGetValue(id, out var task);
        return Task.FromResult(task);
    }

    public Task SaveAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        byId[task.Id] = task;
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IInboundItemBindingStore"/> for the
/// <see cref="AdmitTaskHandler"/> tests. Maps inbound external
/// id to the bound Task id.
/// </summary>
public sealed class InMemoryInboundItemBindingStore : IInboundItemBindingStore
{
    private readonly Dictionary<string, WorkTaskId> byInbound = new(StringComparer.Ordinal);

    public Task<WorkTaskId?> FindByInboundAsync(string inboundItemExternalId, CancellationToken cancellationToken = default)
    {
        return byInbound.TryGetValue(inboundItemExternalId, out var taskId)
            ? Task.FromResult<WorkTaskId?>(taskId)
            : Task.FromResult<WorkTaskId?>(null);
    }

    public Task BindAsync(string inboundItemExternalId, WorkTaskId taskId, CancellationToken cancellationToken = default)
    {
        byInbound[inboundItemExternalId] = taskId;
        return Task.CompletedTask;
    }
}

/// <summary>
/// NSubstitute-backed <see cref="IOutbox"/> that records every
/// call. Tests assert on <c>Received().PublishAsync(...)</c> for
/// the expected (eventType, payload) pair. The shared
/// <c>Shared.Contracts.IOutbox</c> interface is the production
/// seam; the in-process tests reuse it.
/// </summary>
public static class OutboxSpy
{
    public static IOutbox Create()
    {
        return Substitute.For<IOutbox>();
    }

    public static (string eventType, object payload) LastCall(IOutbox outbox)
    {
        var calls = outbox.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IOutbox.PublishAsync))
            .ToList();
        if (calls.Count == 0)
        {
            throw new InvalidOperationException("IOutbox.PublishAsync was never called");
        }

        var call = calls[^1];
        return ((string)call.GetArguments()[0]!, call.GetArguments()[1]!);
    }
}

/// <summary>
/// Convenience factory for the Application-handler T0 tests —
/// builds a fresh handler with the supplied fakes and a
/// deterministic clock. Tests assert on the outbox spy and
/// the in-memory store / inbox. The factory methods that build
/// the dispatch / cancel handlers accept a shared
/// <see cref="InMemoryWorkTaskStore"/> so the cross-handler
/// tests (admit → dispatch → cancel) operate on the same
/// store instance — without this the dispatch / cancel handlers
/// see an empty store and the Task is reported as "not
/// found".
/// </summary>
internal static class TestHandlers
{
    public static (AdmitTaskHandler handler, InMemoryWorkTaskStore tasks, InMemoryWorkInbox inbox, InMemoryInboundItemBindingStore bindings, IOutbox outbox)
        CreateAdmitTaskHandler()
    {
        var tasks = new InMemoryWorkTaskStore();
        var inbox = new InMemoryWorkInbox();
        var bindings = new InMemoryInboundItemBindingStore();
        var outbox = OutboxSpy.Create();
        var clock = TimeProvider.System;
        var handler = new AdmitTaskHandler(
            tasks,
            inbox,
            outbox,
            bindings,
            clock,
            NullLogger<AdmitTaskHandler>.Instance);
        return (handler, tasks, inbox, bindings, outbox);
    }

    public static (DispatchRunHandler handler, IOutbox outbox)
        CreateDispatchRunHandler(InMemoryWorkTaskStore sharedStore)
    {
        ArgumentNullException.ThrowIfNull(sharedStore);
        var outbox = OutboxSpy.Create();
        var clock = TimeProvider.System;
        var handler = new DispatchRunHandler(
            sharedStore,
            outbox,
            clock,
            NullLogger<DispatchRunHandler>.Instance);
        return (handler, outbox);
    }

    public static (CancelAttemptHandler handler, IOutbox outbox)
        CreateCancelAttemptHandler(InMemoryWorkTaskStore sharedStore)
    {
        ArgumentNullException.ThrowIfNull(sharedStore);
        var outbox = OutboxSpy.Create();
        var clock = TimeProvider.System;
        var handler = new CancelAttemptHandler(
            sharedStore,
            outbox,
            clock,
            NullLogger<CancelAttemptHandler>.Instance);
        return (handler, outbox);
    }
}

/// <summary>Sample fixtures for the T0 handler tests.</summary>
internal static class TestFixtures
{
    public static ProjectId Project()
    {
        return new(Guid.CreateVersion7());
    }

    public static WorkDispatchItem Dispatch(WorkTaskId taskId, int attemptOrdinal = 1)
    {
        return new WorkDispatchItem(
            TaskId: taskId.Value.ToString(),
            ProjectId: Project().Value.ToString(),
            AttemptOrdinal: attemptOrdinal,
            ProfileKey: "implement",
            Image: "ghcr.io/comuki/worker@sha256:9f86d0",
            EnvClass: "net10-sdk-bun",
            ProfilesRef: "refs/heads/main",
            Brief: /*lang=json,strict*/ """{"goal":"dispatch a run"}""",
            InboundItemExternalId: "dot-stbl/comuki#89");
    }
}

/// <summary>Backfill runner T0 helper — pairs an in-memory store + binding + a constant inbound source.</summary>
public static class WorkBackfillFixtureFactory
{
    public static (IWorkTaskStore Store, IInboundItemBindingStore Bindings) NewBackfillFixture()
    {
        return (new InMemoryWorkTaskStore(), new InMemoryInboundItemBindingStore());
    }
}
