using Comuki.Shared.Kernel.Ids;
using Comuki.Shared.Kernel.Scoping;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Object-axis semantics of <see cref="SubjectScope"/> and its ambient
/// accessor: the Allows truth table (unrestricted, confined, empty), and
/// the flow discipline — no default scope, Begin/AsSystem restore what
/// was there before, nesting and double dispose are safe.
/// </summary>
public sealed class SubjectScopeShould
{
    [Fact(DisplayName = "Given an unrestricted scope, when Allows is asked for any project, then it is true")]
    public void AllowEverythingWhenUnrestricted()
    {
        var scope = SubjectScope.ForSystem("test-consumer");

        scope.Allows(ProjectId.New()).ShouldBeTrue();
        scope.Allows(ProjectId.New()).ShouldBeTrue();
        scope.Unrestricted.ShouldBeTrue();
        scope.SystemName.ShouldBe("test-consumer");
    }

    [Fact(DisplayName = "Given a scope confined to one project, when Allows is asked, then only that project passes")]
    public void AllowOnlyTheConfinedProject()
    {
        var project = ProjectId.New();
        var scope = new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [project]);

        scope.Allows(project).ShouldBeTrue();
        scope.Allows(ProjectId.New()).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given the empty scope, when Allows is asked for any project, then it is false (fail-closed)")]
    public void AllowNothingWhenEmpty()
    {
        SubjectScope.Nothing.Unrestricted.ShouldBeFalse();
        SubjectScope.Nothing.Allows(ProjectId.New()).ShouldBeFalse();
        SubjectScope.Nothing.ProjectIds.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a scope confined to several projects, when Allows is asked, then each confined project passes")]
    public void AllowEachConfinedProject()
    {
        var first = ProjectId.New();
        var second = ProjectId.New();
        var third = ProjectId.New();
        var scope = new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [first, second, third]);

        scope.Allows(first).ShouldBeTrue();
        scope.Allows(second).ShouldBeTrue();
        scope.Allows(third).ShouldBeTrue();
        scope.Allows(ProjectId.New()).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given no established scope, when Current is read, then it throws rather than defaulting")]
    public void ThrowWhenNoScopeEstablished()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();

        Should.Throw<InvalidOperationException>(() => _ = accessor.Current);
        accessor.CurrentOrNone.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a begun scope, when the handle is disposed, then the flow is left without a scope again")]
    public void RestoreTheNoneStateAfterDispose()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var scope = new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [ProjectId.New()]);

        using (accessor.Begin(scope))
        {
            accessor.Current.ShouldBe(scope);
        }

        Should.Throw<InvalidOperationException>(() => _ = accessor.Current);
    }

    [Fact(DisplayName = "Given an established scope, when AsSystem is entered and left, then the previous scope is back")]
    public void RestoreThePreviousScopeAfterSystem()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var scope = new SubjectScope(Unrestricted: false, SystemName: null, ProjectIds: [ProjectId.New()]);
        using var outer = accessor.Begin(scope);

        using (accessor.AsSystem("lease-reaper"))
        {
            accessor.Current.Unrestricted.ShouldBeTrue();
            accessor.Current.SystemName.ShouldBe("lease-reaper");
        }

        accessor.Current.ShouldBe(scope);
    }

    [Fact(DisplayName = "Given a disposed handle, when it is disposed again, then it does not overwrite a scope entered after it")]
    public void DoubleDisposeIsANoOp()
    {
        var accessor = new AsyncLocalSubjectScopeAccessor();
        var first = accessor.AsSystem("first-consumer");

        first.Dispose();
        using (accessor.AsSystem("second-consumer"))
        {
            first.Dispose();
            accessor.Current.SystemName.ShouldBe("second-consumer");
        }
    }
}
