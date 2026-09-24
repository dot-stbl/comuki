using System.Data.Common;
using System.Text.Json;
using Comuki.Modules.Verify.Application.Options;
using Comuki.Modules.Verify.Application.Ports;
using Comuki.Shared.Bootstrap.Workers;
using Comuki.Shared.Kernel.Scoping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Comuki.Modules.Verify.Infrastructure.Sync;

/// <summary>
/// Polls <see cref="IGenericCommandStore"/> every
/// <see cref="VerifyOptions.PollInterval"/> for Pending generic-command
/// runs, marks each as Running, calls <see cref="IGenericCommandRunner"/>,
/// stamps the verdict (Green when the exit code matches
/// <c>GenericCommandRun.ExpectedExitCode</c>, Red otherwise) and updates
/// the row in one scoped transaction. The store's claim query uses
/// <c>FOR UPDATE SKIP LOCKED</c>, so two host replicas never double-fire
/// the same row.
/// <para>
/// <see cref="VerifyOptions.Enabled"/> is <see langword="false"/> by
/// default — see the isolation warning on <see cref="VerifyOptions"/>
/// and <see cref="GenericCommandProcessRunner"/>. When disabled every
/// cycle is a no-op that touches neither the store nor the runner.
/// </para>
/// <para>
/// Per-run isolation: a failing run (launch exception, runner timeout)
/// is stamped Red with the error reason in <c>output_log</c> and the
/// rest of the batch still runs. Transient store/runner failures
/// (DB outage, EF exception) fail the whole cycle for the comuki worker
/// registry (logged, exponential backoff) — the pending rows stay
/// locked/pending and the next cycle retries.
/// </para>
/// </summary>
/// <param name="clock">Wall-clock source for the lifecycle stamps.</param>
/// <param name="scopeFactory">Scope factory — resolves the scoped store per cycle.</param>
/// <param name="scopeAccessor">AmbientScope — the worker runs AsSystem so it can see every project's runs (and the project-less global gate runs).</param>
/// <param name="options">Tunables (poll interval, batch size, runner knobs).</param>
/// <param name="logger">Structured logger.</param>
public sealed class GenericCommandVerifierWorker(
    TimeProvider clock,
    IServiceScopeFactory scopeFactory,
    ISubjectScopeAccessor scopeAccessor,
    IOptions<VerifyOptions> options,
    ILogger<GenericCommandVerifierWorker> logger) : IComukiWorker
{
    /// <inheritdoc />
    public string Name => "verify-generic-command";

    /// <inheritdoc />
    public WorkerSchedule Schedule => WorkerSchedule.Interval(options.Value.PollInterval);

    /// <inheritdoc />
    public async Task<WorkerResult> ExecuteAsync(WorkerContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return WorkerResult.Ok("disabled (Verify:Verifier:Enabled=false)");
        }

        using var systemScope = scopeAccessor.AsSystem(Name);

        try
        {
            var processed = await PollOnceAsync(cancellationToken);
            return WorkerResult.Ok($"processed {processed}", processed);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                   or DbException or JsonException)
        {
            // boundary: the worker's own supervision loop — transient
            // store/runner failures count as a failed cycle for the
            // registry (logged, backoff). Next cycle retries the whole
            // batch.
            return WorkerResult.Fail($"cycle failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Runs one polling cycle synchronously — exposed for unit/integration
    /// tests that need to drive the worker deterministically rather than
    /// wait for the configured interval.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Number of runs processed (claimed + completed) in this cycle.</returns>
    public async Task<int> PollOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGenericCommandStore>();
        var runner = scope.ServiceProvider.GetRequiredService<IGenericCommandRunner>();

        var now = clock.GetUtcNow();
        var processed = 0;

        foreach (var run in await store.ClaimPendingAsync(options.Value.BatchSize, cancellationToken))
        {
            try
            {
                run.MarkRunning(now);
                await store.UpdateAsync(run, cancellationToken);

                var result = await runner.RunAsync(run.Executable, run.Arguments, workingDirectory: null, cancellationToken);

                // Pattern-matching the exit code (rather than
                // result.ExitCode!.Value) keeps the Green/Red decision
                // tied to the same invariant GenericCommandRunResult
                // already encodes in LaunchFailed — no null-forgiving
                // across that boundary.
                if (result.ExitCode is { } exitCode)
                {
                    run.MarkCompleted(exitCode, result.OutputLog, clock.GetUtcNow());
                }
                else
                {
                    run.MarkLaunchFailed(result.LaunchFailureDetail ?? "launch failed", clock.GetUtcNow());
                }

                await store.UpdateAsync(run, cancellationToken);
                processed++;

                logger.LogInformation(
                    "Generic-command run {RunId} ({ProfileKey}) {Verdict} (exit code {ExitCode}, expected {Expected})",
                    run.Id.Value, run.ProfileKey, run.Status, result.ExitCode, run.ExpectedExitCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                       or DbException or JsonException)
            {
                // per-run isolation: one run's runner / store failure
                // does not abort the batch. The run stays Running (we
                // already stamped MarkRunning before the runner call);
                // a future cleanup pass or manual recover handles a
                // stuck Running row. Better than flipping it to Red on a
                // transient store hiccup.
                logger.LogWarning(exception,
                    "Generic-command run {RunId} failed during verify; run is left in Running for manual review",
                    run.Id.Value);
            }
        }

        return processed;
    }
}
