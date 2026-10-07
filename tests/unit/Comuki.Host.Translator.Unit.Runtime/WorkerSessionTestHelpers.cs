using System.Runtime.CompilerServices;
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
    /// <summary>Mocks <see cref="IWorkerService.Connect"/> to return <paramref name="commands"/>.</summary>
    /// <param name="service"></param>
    /// <param name="commands"></param>
    public static void StubCommandStream(IWorkerService service, IEnumerable<OrchestratorCommand> commands)
    {
        service.Connect(Arg.Any<IAsyncEnumerable<WorkerEvent>>(), Arg.Any<CallContext>())
            .Returns(_ => StreamCommandsAsync(commands));
    }

    /// <summary>Async-enumerable that yields each element and completes.</summary>
    /// <param name="source"></param>
    /// <param name="cancellationToken">Stops the enumeration between yields.</param>
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
    /// <param name="harnessSession">
    /// Optional harness session the command handler reads when a
    /// <see cref="TurnInput"/> arrives.
    /// <c>null</c> (the default) preserves the existing pre-Phase 1c
    /// behaviour where the command handler logs the receipt and
    /// drops the turn (the harness session was not yet part of
    /// the run). Pass a <c>TestFakeHarness</c> session to exercise
    /// the actual session-mode write path.
    /// </param>
    /// <param name="runStartedAt"></param>
    /// <param name="processStartedAt"></param>
    public static WorkerRun NewRun(
        IWorkerService service,
        Guid workItemId,
        CancellationTokenSource runCancellation,
        IHarnessSession? harnessSession = null,
        DateTimeOffset? runStartedAt = null,
        DateTimeOffset? processStartedAt = null)
    {
        var claimed = new ClaimedWorkItemResponse(
            workItemId,
            RunId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            ProfileKey: "test-profile",
            EnvClass: "net10-sdk-bun",
            Brief: "test-brief",
            LeaseUntilUnixMs: 0,
            Attempt: 1,
            Generation: 1);
        var session = WorkerSession.Open(service, "test-token");
        // Harden-worker-runtime Phase 1 added RunStartedAt and
        // ProcessStartedAt as required WorkerRun fields. Tests that
        // don't exercise the watchdog / deadline policy can leave
        // them as DateTimeOffset defaults; watchdog / policy tests
        // pass explicit values through the new optional parameters.
        return new WorkerRun(claimed, session)
        {
            RunCancellation = runCancellation,
            HarnessSession = harnessSession,
            RunStartedAt = runStartedAt ?? DateTimeOffset.MinValue,
            ProcessStartedAt = processStartedAt ?? DateTimeOffset.MinValue,
        };
    }

    /// <summary>Builds a fully-populated <see cref="TranslatorOptions"/> with the
    /// required base fields set to test fakes; the watchdog / policy
    /// tests override the timeout / policy values they exercise.</summary>
    /// <param name="workerProgressTimeout">Override for the progress watchdog's stall threshold.</param>
    /// <param name="turnBudget">Override for the per-cycle wall-clock budget.</param>
    /// <param name="runBudget">Override for the per-process wall-clock budget.</param>
    /// <param name="policy">Override for the progress-watchdog escalation policy.</param>
    /// <param name="consecutiveTurnBreachesBeforeFail">Override for the turn-budget chain threshold.</param>
    public static TranslatorOptions NewOptions(
        TimeSpan? workerProgressTimeout = null,
        TimeSpan? turnBudget = null,
        TimeSpan? runBudget = null,
        WorkerProgressEscalationPolicy? policy = null,
        int? consecutiveTurnBreachesBeforeFail = null)
    {
        return new TranslatorOptions
        {
            OrchestratorBaseUrl = new Uri("http://localhost:0/"),
            OrchestratorGrpcUrl = new Uri("http://localhost:0/"),
            WorkerToken = "test-worker-token-1234567890",
            ProfileKey = "test-profile",
            ProfilesRef = "main",
            WorkerImage = "test-image",
            WorkerProgressTimeout = workerProgressTimeout ?? TimeSpan.FromSeconds(60),
            TurnBudget = turnBudget ?? TimeSpan.FromMinutes(60),
            RunBudget = runBudget ?? TimeSpan.FromHours(8),
            WorkerProgressEscalationPolicy = policy ?? WorkerProgressEscalationPolicy.WarnGentleKillFailItem,
            ConsecutiveTurnBreachesBeforeFail = consecutiveTurnBreachesBeforeFail ?? 3,
        };
    }
}
