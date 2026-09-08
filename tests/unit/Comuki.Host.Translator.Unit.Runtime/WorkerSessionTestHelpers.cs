using System.Runtime.CompilerServices;
using Comuki.Host.Translator.Api.Models.Responses;
using Comuki.Host.Translator.Execution.Run;
using Comuki.Host.Translator.Grpc;
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
    /// <summary>Mocks <see cref="IWorkerService.Connect"/> to return <paramref name="commands"/>.</summary>
    /// <param name="service"></param>
    /// <param name="commands"></param>
    public static void StubCommandStream(IWorkerService service, IEnumerable<OrchestratorCommand> commands)
    {
        _ = service.Connect(Arg.Any<IAsyncEnumerable<WorkerEvent>>(), Arg.Any<CallContext>())
            .Returns(_ => StreamCommandsAsync(commands));
    }

    /// <summary>Async-enumerable that yields each element and completes.</summary>
    /// <param name="source"></param>
    private static async IAsyncEnumerable<T> StreamCommandsAsync<T>(
        IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }

    /// <summary>Builds a <see cref="WorkerRun"/> around a real <see cref="WorkerSession"/> opened via the mock service.</summary>
    /// <param name="service"></param>
    /// <param name="workItemId"></param>
    /// <param name="runCancellation"></param>
    public static WorkerRun NewRun(
        IWorkerService service,
        Guid workItemId,
        CancellationTokenSource runCancellation)
    {
        var claimed = new ClaimedWorkItemResponse(
            workItemId,
            RunId: Guid.NewGuid(),
            ProfileKey: "test-profile",
            Brief: "test-brief",
            LeaseUntilUnixMs: 0,
            Attempt: 1);
        var session = WorkerSession.Open(service, "test-token");
        return new WorkerRun(claimed, session) { RunCancellation = runCancellation };
    }
}
