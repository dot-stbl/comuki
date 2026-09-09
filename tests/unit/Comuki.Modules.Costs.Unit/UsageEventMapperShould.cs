using System.Globalization;
using Comuki.Modules.Costs.Application.Views;
using Comuki.Shared.Contracts.Usage;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Costs.Unit;

/// <summary>DTO → view mapping for usage events.</summary>
public sealed class UsageEventMapperShould
{
    [Fact(DisplayName = "Given a usage event summary, when ToView is called, then fields are copied")]
    public void MapFields()
    {
        var runId = RunId.New();
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z", CultureInfo.InvariantCulture);
        var summary = new UsageEventSummary(
            Guid.NewGuid(),
            runId,
            UsageSources.Proxy,
            "claude-sonnet-4",
            11,
            22,
            33_000,
            now);

        var view = UsageEventMapper.ToView(summary);

        view.Id.ShouldBe(summary.Id);
        view.RunId.ShouldBe(runId);
        view.Source.ShouldBe(UsageSources.Proxy);
        view.Model.ShouldBe("claude-sonnet-4");
        view.InputTokens.ShouldBe(11);
        view.OutputTokens.ShouldBe(22);
        view.CostUsdMicros.ShouldBe(33_000);
        view.OccurredAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a usage event summary without run, when ToView is called, then RunId is null")]
    public void MapWithoutRun()
    {
        var summary = new UsageEventSummary(
            Guid.NewGuid(),
            null,
            UsageSources.System,
            "model",
            0,
            0,
            1,
            DateTimeOffset.UtcNow);

        UsageEventMapper.ToView(summary).RunId.ShouldBeNull();
    }
}
