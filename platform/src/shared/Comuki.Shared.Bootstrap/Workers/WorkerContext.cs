using Microsoft.Extensions.Logging;

namespace Comuki.Shared.Bootstrap.Workers;

/// <summary>
/// What one <see cref="IComukiWorker.ExecuteAsync"/> invocation receives:
/// a scoped service provider (the registry opens a fresh scope per cycle
/// so scoped stores die with the cycle), the clock, and a logger named
/// after the worker.
/// </summary>
/// <param name="Services">Scoped services for this execution; dispose happens with the scope.</param>
/// <param name="Clock">The host clock.</param>
/// <param name="Logger">A logger categorised as <c>{worker-name}</c>.</param>
public sealed record WorkerContext(IServiceProvider Services, TimeProvider Clock, ILogger Logger);
