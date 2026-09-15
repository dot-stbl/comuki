using System.Collections.Concurrent;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// Trivial per-worker sliding-window limiter for <c>memory.note</c> — the
/// write path a swarm worker has, so a runaway loop spamming facts cannot
/// flood the project corpus. In-memory by design (a worker container is
/// single-instance; the window only needs to be per-process accurate),
/// lock-free over a concurrent queue of admitted timestamps.
/// </summary>
/// <param name="clock"></param>
public sealed class WorkerNoteRateLimiter(TimeProvider clock)
{
    /// <summary>How many notes one worker may write per window.</summary>
    public const int Limit = 20;

    /// <summary>The sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<WorkerId, Queue<DateTimeOffset>> admittedByWorker = new();

    /// <summary>
    /// Admits one note for the worker; false when the worker is at
    /// <see cref="Limit"/> inside the current <see cref="Window"/>.
    /// </summary>
    /// <param name="workerId"></param>
    public bool TryAcquire(WorkerId workerId)
    {
        var now = clock.GetUtcNow();
        var queue = admittedByWorker.GetOrAdd(workerId, static _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() <= now - Window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= Limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
