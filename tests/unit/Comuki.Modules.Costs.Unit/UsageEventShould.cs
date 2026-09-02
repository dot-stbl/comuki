using System.Globalization;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Domain factory invariants for <see cref="UsageEvent"/>.</summary>
public sealed class UsageEventShould
{
    [Fact(DisplayName = "Given valid inputs, when Create is called, then fields are set")]
    public void CreateWithValidInputs()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z", CultureInfo.InvariantCulture);

        var usageEvent = UsageEvent.Create(
            projectId,
            runId,
            UsageSource.Proxy,
            "  claude-sonnet-4  ",
            100,
            50,
            12_500,
            now);

        usageEvent.ProjectId.ShouldBe(projectId);
        usageEvent.RunId.ShouldBe(runId);
        usageEvent.Source.ShouldBe(UsageSource.Proxy);
        usageEvent.Model.ShouldBe("claude-sonnet-4");
        usageEvent.InputTokens.ShouldBe(100);
        usageEvent.OutputTokens.ShouldBe(50);
        usageEvent.CostUsdMicros.ShouldBe(12_500);
        usageEvent.OccurredAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given empty model, when Create is called, then throws")]
    public void RefuseEmptyModel()
    {
        Should.Throw<ArgumentException>(static () => UsageEvent.Create(
            ProjectId.New(),
            null,
            UsageSource.Brain,
            "  ",
            1,
            0,
            1,
            DateTimeOffset.UtcNow));
    }

    [Fact(DisplayName = "Given negative cost, when Create is called, then throws")]
    public void RefuseNegativeCost()
    {
        Should.Throw<ArgumentOutOfRangeException>(static () => UsageEvent.Create(
            ProjectId.New(),
            null,
            UsageSource.Worker,
            "model",
            0,
            0,
            -1,
            DateTimeOffset.UtcNow));
    }
}
