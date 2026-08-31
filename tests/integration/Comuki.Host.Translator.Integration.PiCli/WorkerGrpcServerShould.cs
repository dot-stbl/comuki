using Comuki.Engine.Compute.Security;
using Comuki.Host.Workers;
using Comuki.Host.Workers.Grpc;
using Comuki.Shared.Contracts.Grpc;
using Comuki.Shared.Contracts.Journal;
using Comuki.Shared.Kernel.Ids;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Integration.PiCli;

/// <summary>
/// Server-side integration of <see cref="WorkerGrpcService"/> over a real
/// loopback gRPC channel: token authentication, event journaling, and
/// command delivery through the <see cref="IWorkerCommandPipe"/>.
/// </summary>
public sealed class WorkerGrpcServerShould : IAsyncLifetime
{
    private readonly InMemoryRunJournal journal = new();

    private TestWorkerHost host = null!;

    private string workerToken = null!;

    private WorkerId workerId;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        host = await TestWorkerHost.StartAsync(services =>
        {
            services.AddSingleton<IRunJournal>(journal);
            services.AddWorkerRuntime(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        }, mapRest: false);

        var issuer = host.GetService<WorkerTokenIssuer>();
        workerId = WorkerId.New();
        workerToken = issuer.Issue(workerId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync();
    }

    private IWorkerService CreateClient()
    {
        var channel = GrpcChannel.ForAddress(host.GrpcAddress);
        return GrpcClientFactory.CreateGrpcService<IWorkerService>(channel);
    }
    /// <summary>
    /// Yields the given events, then keeps the request stream OPEN (a real
    /// translator holds it for the whole pi session) until the caller's
    /// completion source fires or the test cancels — so the server cannot
    /// close the call on events-completion before the Stop is delivered.
    /// </summary>
    private static IAsyncEnumerable<WorkerEvent> EventsOf(TaskCompletionSource done, params WorkerEvent[] events)
    {
        return EventsOfAsync(done, events);
    }

    private static async IAsyncEnumerable<WorkerEvent> EventsOfAsync(
        TaskCompletionSource done,
        WorkerEvent[] events)
    {
        foreach (var workerEvent in events)
        {
            yield return workerEvent;
        }

        await done.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JournalStreamedEventsAndDeliverStopCommandAsync()
    {
        var runId = Guid.NewGuid();
        var workItemId = Guid.NewGuid();
        var eventsDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateClient();
        var commandsTask = Task.Run(async () =>
        {
            var received = new List<OrchestratorCommand>();
            await foreach (var command in client.Connect(
                EventsOf(
                    eventsDone,
                    new WorkerEvent { Start = new StageStart { WorkItemId = workItemId.ToString(), RunId = runId.ToString(), Brief = "do it" } },
                    new WorkerEvent { Activity = new StageActivity { WorkItemId = workItemId.ToString(), Text = "working" } }),
                new ProtoBuf.Grpc.CallContext(new CallOptions(new Metadata { { "authorization", workerToken } }))))
            {
                received.Add(command);
                if (command.Stop is not null)
                {
                    break;
                }
            }

            return received;
        });

        // the stream registers on the hub only after the server starts
        // consuming — poll until the worker is connected, then stop it
        var commandPipe = host.GetService<IWorkerCommandPipe>();
        var delivered = false;
        for (var attempt = 0; attempt < 100 && !delivered; attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            delivered = commandPipe.TrySendStop(workerId, "user cancelled");
        }

        eventsDone.TrySetResult();
        delivered.ShouldBeTrue("stop should reach the connected worker");
        var commands = await commandsTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        commands.ShouldContain(command => command.Stop != null);

        var entries = await journal.ReadAllAsync();
        entries.ShouldContain(entry => entry.RunId.Value == runId && entry.PayloadJson.Contains("do it", StringComparison.Ordinal));
        entries.ShouldContain(entry => entry.PayloadJson.Contains("working", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectUnknownTokenAsync()
    {
        var client = CreateClient();
        var call = client.Connect(
            EventsOf(
                CompletedEventsSource.CompletedEvents(),
                new WorkerEvent { Activity = new StageActivity { Text = "sneak" } }),
            new ProtoBuf.Grpc.CallContext(new CallOptions(new Metadata { { "authorization", "not-a-real-token" } })));

        await Should.ThrowAsync<RpcException>(async () =>
        {
            await foreach (var _ in call)
            {
            }
        });
    }

    [Fact]
    public async Task DropEventsBeforeStageStartAsync()
    {
        var client = CreateClient();
        var runId = Guid.NewGuid();
        await foreach (var _ in client.Connect(
            EventsOf(
                CompletedEventsSource.CompletedEvents(),
                new WorkerEvent { Activity = new StageActivity { Text = "orphan" } },
                new WorkerEvent { Start = new StageStart { WorkItemId = Guid.NewGuid().ToString(), RunId = runId.ToString(), Brief = "late" } }),
            new ProtoBuf.Grpc.CallContext(new CallOptions(new Metadata { { "authorization", workerToken } }))))
        {
        }

        var entries = await journal.ReadAllAsync();
        entries.ShouldNotContain(static entry => entry.PayloadJson.Contains("orphan", StringComparison.Ordinal));
        entries.ShouldContain(static entry => entry.PayloadJson.Contains("late", StringComparison.Ordinal));
    }
}

/// <summary>A completion source that is already done — the events stream closes right after yielding.</summary>
file static class CompletedEventsSource
{
    public static TaskCompletionSource CompletedEvents()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

/// <summary>Captures journal appends in memory for assertions.</summary>
internal sealed class InMemoryRunJournal : IRunJournal
{
    private readonly List<RunEventEntry> entries = [];

    public Task AppendAsync(RunEventEntry entry, CancellationToken cancellationToken = default)
    {
        lock (entries)
        {
            entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunEventEntry>> ReadTimelineAsync(RunId runId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RunEventEntry>>([.. entries.Where(entry => entry.RunId == runId)]);
    }

    public Task<IReadOnlyList<RunEventEntry>> ReadAllAsync()
    {
        lock (entries)
        {
            return Task.FromResult<IReadOnlyList<RunEventEntry>>([.. entries]);
        }
    }
}
