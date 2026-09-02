using System.Globalization;
using Comuki.Modules.Costs.Application.Views;
using Comuki.Modules.Costs.Domain.Events;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>Entity → view mapping for usage events.</summary>
public sealed class UsageEventMapperShould
{
    [Fact(DisplayName = "Given a usage event, when ToView is called, then fields and wire source key are copied")]
    public void MapFields()
    {
        var projectId = ProjectId.New();
        var runId = RunId.New();
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z", CultureInfo.InvariantCulture);
        var usageEvent = UsageEvent.Create(
            projectId,
            runId,
            UsageSource.Proxy,
            "claude-sonnet-4",
            11,
            22,
            33_000,
            now);

        var view = UsageEventMapper.ToView(usageEvent);

        view.Id.ShouldBe(usageEvent.Id.Value);
        view.RunId.ShouldBe(runId);
        view.Source.ShouldBe(UsageSourceKeys.Proxy);
        view.Model.ShouldBe("claude-sonnet-4");
        view.InputTokens.ShouldBe(11);
        view.OutputTokens.ShouldBe(22);
        view.CostUsdMicros.ShouldBe(33_000);
        view.OccurredAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a usage event without run, when ToView is called, then RunId is null")]
    public void MapWithoutRun()
    {
        var usageEvent = UsageEvent.Create(
            ProjectId.New(),
            null,
            UsageSource.System,
            "model",
            0,
            0,
            1,
            DateTimeOffset.UtcNow);

        UsageEventMapper.ToView(usageEvent).RunId.ShouldBeNull();
    }
}
