namespace Comuki.AgentTest.Runner.Execution.Support;

/// <summary>
/// Poll-with-timeout helper — never a bare <c>Task.Delay</c> assumption
/// (canon <c>testing-integration.md</c> §6). Deliberately not a dependency
/// on <c>Comuki.Host.Testing</c>'s own copy: this library has no xUnit
/// dependency at all (it is meant to stay usable outside a test runner —
/// WS8/WS9's <c>scripts/ci/*.mjs</c> entry points shell out to it).
/// </summary>
public static class WaitForAsync
{
    /// <summary>
    /// Polls <paramref name="condition"/> every <paramref name="pollInterval"/>
    /// until it returns true or <paramref name="timeout"/> elapses.
    /// </summary>
    /// <param name="condition">Returns true once the awaited state holds.</param>
    /// <param name="timeout">Total time budget.</param>
    /// <param name="pollInterval">Delay between polls.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True if <paramref name="condition"/> became true within the budget; false on timeout.</returns>
    public static async Task<bool> PollAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await condition())
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }
}
