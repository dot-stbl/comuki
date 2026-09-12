using Comuki.Shared.Bootstrap.Correlation;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Correlation;

/// <summary>
/// The AsyncLocal correlation accessor (issue #56 §5): Begin installs the
/// id on the flow, Dispose restores the previous value, and two parallel
/// flows never see each other's id.
/// </summary>
public sealed class AsyncLocalCorrelationIdAccessorShould
{
    [Fact(DisplayName = "Given no scope, when CurrentId is read, then it is null")]
    public void NullWithoutScope()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();

        accessor.CurrentId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a begun scope, when CurrentId is read inside, then the id is visible and restored after dispose")]
    public void BeginInstallsAndDisposeRestores()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();

        using (accessor.Begin("req-12345678"))
        {
            accessor.CurrentId.ShouldBe("req-12345678");
        }

        accessor.CurrentId.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a nested scope, when the inner disposes, then the outer id returns")]
    public void NestedScopesRestoreOuterId()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();

        using (accessor.Begin("outer-id"))
        {
            using (accessor.Begin("inner-id"))
            {
                accessor.CurrentId.ShouldBe("inner-id");
            }

            accessor.CurrentId.ShouldBe("outer-id");
        }
    }

    [Fact(DisplayName = "Given two parallel flows, when each begins its own id, then neither flow observes the other")]
    public async Task ParallelFlowsAreIsolatedAsync()
    {
        var accessor = new AsyncLocalCorrelationIdAccessor();
        using var ambient = accessor.Begin("ambient-id");

        var observed = await Task.WhenAll(
            FlowScopeAsync(accessor, "first-request"),
            FlowScopeAsync(accessor, "second-request"));

        observed.ShouldBe(["first-request", "second-request"]);
        accessor.CurrentId.ShouldBe("ambient-id");
    }

    private static async Task<string> FlowScopeAsync(AsyncLocalCorrelationIdAccessor accessor, string requestId)
    {
        using var scope = accessor.Begin(requestId);
        await Task.Yield();
        return accessor.CurrentId ?? "none";
    }
}
