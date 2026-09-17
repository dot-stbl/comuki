using System.Collections.Concurrent;
using Comuki.Shared.Kernel.Ids;

namespace Comuki.Host.Mcp;

/// <summary>
/// Per-worker sliding-window limiter for <c>learning.suggest</c> — the
/// learning queue is human-reviewed, so a runaway worker spamming
/// suggestions floods the approvers' queue, not just its own project. The
/// mission budget is "max 5 per worker per run"; the caller carries no run
/// id, so the closest per-process approximation is the same shape as
/// <see cref="WorkerNoteRateLimiter"/> — a per-worker sliding window with a
/// tight limit over a window long enough to span a typical run.
/// </summary>
/// <param name="clock"></param>
public sealed class WorkerSuggestRateLimiter(TimeProvider clock)
{
    /// <summary>How many suggestions one worker may queue per window.</summary>
    public const int Limit = 5;

    /// <summary>The sliding window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<WorkerId, Queue<DateTimeOffset>> admittedByWorker = new();

    /// <summary>
    /// Admits one suggestion for the worker; false when the worker is at
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
