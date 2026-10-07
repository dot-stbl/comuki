using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Grpc;
using Comuki.Host.Translator.Runtime;
using Comuki.Shared.Contracts.Grpc;
using NSubstitute;
using ProtoBuf.Grpc;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Helpers that build the gRPC surface WorkerSession needs from a mock
/// <see cref="IWorkerService"/>: an in-memory stream of orchestrator commands
/// and a consumed-sink stream for the worker events. Lets unit tests
/// drive the bidi stream without standing up a real gRPC server.
/// </summary>
internal static class WorkerSessionTestHelpers
{
    /// <summary>
    /// Mocks <see cref="IWorkerService.Connect"/> to return a hand-rolled
    /// command stream (the <paramref name="commands"/> argument, used as
    /// the producer of the response IAsyncEnumerable).
    /// </summary>
    /// <remarks>
    /// Two paths supported: an NSubstitute proxy (existing call sites
    /// — <see cref="WorkerCommandHandlerShould"/> drives a real
    /// orchestrator command through the proxy) and a
    /// <see cref="StubCommandStreamTarget"/> hand-rolled substitute
    /// (used by <see cref="PiPumpShould"/>, which never reads the
    /// command stream — NSubstitute's Castle-proxy returns a
    /// non-iterable <see cref="IAsyncEnumerable{T}"/> in that case and
    /// the command pump hangs).
    /// </remarks>
    /// <param name="service">Substitute of <see cref="IWorkerService"/>; either NSubstitute proxy or <see cref="StubCommandStreamTarget"/>.</param>
    /// <param name="commands">Commands the test wants WorkerSession to read.</param>
    public static void StubCommandStream(IWorkerService service, IEnumerable<OrchestratorCommand> commands)
    {
        switch (service)
        {
            case StubCommandStreamTarget target:
                target.Commands = [.. commands];
                return;
            default:
                var interceptor = new ConnectInterceptor(commands);
                service.Connect(Arg.Any<IAsyncEnumerable<WorkerEvent>>(), Arg.Any<CallContext>())
                    .Returns(ci => interceptor.Connect(ci.ArgAt<IAsyncEnumerable<WorkerEvent>>(0), ci.ArgAt<CallContext>(1)));
                return;
        }
    }

    /// <summary>
    /// One-method <see cref="IWorkerService"/> used as the substitute's
    /// callback target. <see cref="Connect"/> returns a hand-rolled
    /// <see cref="IAsyncEnumerable{T}"/> that yields <paramref name="commands"/>
    /// then completes — the test's authoritative command stream, not
    /// something the NSubstitute proxy has to manufacture.
    /// </summary>
    /// <param name="commands">Commands to yield, in order, on Connect.</param>
    private sealed class ConnectInterceptor(IEnumerable<OrchestratorCommand> commands)
    {
        /// <summary>Returns the hand-rolled command enumerator.</summary>
        /// <param name="events">Worker event producer — ignored, no inbound is read.</param>
        /// <param name="context">gRPC call context — ignored, the test is in-process.</param>
        public IAsyncEnumerable<OrchestratorCommand> Connect(
            IAsyncEnumerable<WorkerEvent> events,
            CallContext context)
        {
            return new CommandEnumerable(commands);
        }
    }

    /// <summary>
    /// Hand-rolled <see cref="IAsyncEnumerable{T}"/> + enumerator over a
    /// fixed list. Avoids the <c>async</c> state machine (which NSubstitute's
    /// Castle proxy mishandles for an empty source) by tracking the index
    /// and completion directly.
    /// </summary>
    /// <param name="source">The fixed command list to yield.</param>
    private sealed class CommandEnumerable(IEnumerable<OrchestratorCommand> source) : IAsyncEnumerable<OrchestratorCommand>
    {
        public IAsyncEnumerator<OrchestratorCommand> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new CommandEnumerator([.. source], cancellationToken);
        }
    }

    /// <summary>
    /// Index-based enumerator over the snapshot list. Returns
    /// <c>false</c> from <see cref="MoveNextAsync"/> when the index runs
    /// past the end — empty list, zero yields, immediate completion.
    /// </summary>
    /// <param name="items">The command snapshot.</param>
    /// <param name="cancellationToken">Stops the enumeration between yields.</param>
    private sealed class CommandEnumerator(IReadOnlyList<OrchestratorCommand> items, CancellationToken cancellationToken) : IAsyncEnumerator<OrchestratorCommand>
    {
        private int index = -1;

        public OrchestratorCommand Current => items[index];

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> MoveNextAsync()
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(false);
            }

            index++;
            return ValueTask.FromResult(index < items.Count);
        }
    }

    /// <summary>Builds a <see cref="WorkerRun"/> around a real <see cref="WorkerSession"/> opened via the mock service.</summary>
    /// <param name="service"></param>
    /// <param name="workItemId"></param>
    /// <param name="runCancellation"></param>
    /// <param name="harnessSession">
    /// Optional harness session the command handler reads when a
    /// <see cref="TurnInput"/> arrives.
    /// <c>null</c> (the default) preserves the existing pre-Phase 1c
    /// behaviour where the command handler logs the receipt and
    /// drops the turn (the harness session was not yet part of
    /// the run). Pass a <c>TestFakeHarness</c> session to exercise
    /// the actual session-mode write path.
    /// </param>
    public static WorkerRun NewRun(
        IWorkerService service,
        Guid workItemId,
        CancellationTokenSource runCancellation,
        IHarnessSession? harnessSession = null)
    {
        var claimed = new ClaimedWorkItemResponse(
            workItemId,
            RunId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            ProfileKey: "test-profile",
            EnvClass: NetTenSdkBunEnvClass,
            Brief: "test-brief",
            LeaseUntilUnixMs: 0,
            Attempt: 1,
            Generation: 1);
        var session = WorkerSession.Open(service, "test-token");
        return new WorkerRun(claimed, session) { RunCancellation = runCancellation, HarnessSession = harnessSession };
    }

    /// <summary>
    /// Canonical <c>net10-sdk-bun</c> env class the fixture seeds. Sourced
    /// from the integration tests' <c>EnvClass</c> constant — both surfaces
    /// have to agree or the host's claim rejector marks the work item as
    /// out-of-scope before WorkerRun is even built. Centralised here so a
    /// rename in either place fails the build.
    /// </summary>
    public const string NetTenSdkBunEnvClass = "net10-sdk-bun";
}

/// <summary>
/// Hand-rolled <see cref="IWorkerService"/> substitute. The default
/// substitute (NSubstitute + Castle) returns a non-iterable
/// <see cref="IAsyncEnumerable{T}"/> when asked for a command stream —
/// its <c>GetAsyncEnumerator</c> proxy returns a default enumerator
/// that hangs on the first <c>MoveNextAsync</c>. This type avoids the
/// proxy by implementing <see cref="IWorkerService"/> directly and
/// returning the same hand-rolled <c>CommandEnumerablePublic</c>
/// used by the helper's NSubstitute path. Use it from tests that
/// don't drive the command stream (<c>PiPumpShould</c>) and the
/// command pump is the one piece of the bidi channel that has to
/// complete cleanly without anything reading it.
/// </summary>
public sealed class StubCommandStreamTarget : IWorkerService
{
    /// <summary>Commands the worker's <see cref="WorkerSession"/> should read.</summary>
    public IReadOnlyList<OrchestratorCommand> Commands { get; set; } = [];

    /// <inheritdoc />
    public IAsyncEnumerable<OrchestratorCommand> Connect(IAsyncEnumerable<WorkerEvent> events, CallContext context)
    {
        return new CommandEnumerablePublic(Commands);
    }
}

/// <summary>Public-facing mirror of the helper's hand-rolled <see cref="IAsyncEnumerable{T}"/>.</summary>
/// <param name="source">The fixed command list to yield.</param>
public sealed class CommandEnumerablePublic(IReadOnlyList<OrchestratorCommand> source) : IAsyncEnumerable<OrchestratorCommand>
{
    /// <inheritdoc />
    public IAsyncEnumerator<OrchestratorCommand> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new CommandEnumeratorPublic(source, cancellationToken);
    }
}

/// <summary>Index-based enumerator over a snapshot list. <c>false</c> on the index past the end.</summary>
/// <param name="items">The command snapshot.</param>
/// <param name="cancellationToken">Stops the enumeration between yields.</param>
public sealed class CommandEnumeratorPublic(IReadOnlyList<OrchestratorCommand> items, CancellationToken cancellationToken) : IAsyncEnumerator<OrchestratorCommand>
{
    private int index = -1;

    /// <inheritdoc />
    public OrchestratorCommand Current => items[index];

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<bool> MoveNextAsync()
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(false);
        }

        index++;
        return ValueTask.FromResult(index < items.Count);
    }
}
