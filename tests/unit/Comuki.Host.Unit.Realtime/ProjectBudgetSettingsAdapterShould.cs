using Comuki.Host.Costs;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.Realtime;

/// <summary>Host adapter: Projects settings → budget caps.</summary>
public sealed class ProjectBudgetSettingsAdapterShould
{
    [Fact(DisplayName = "Given a cached settings row, when GetAsync, then soft/hard caps are returned without FindAsync")]
    public async Task PreferCachedSettingsAsync()
    {
        var projectId = ProjectId.New();
        var settings = Substitute.For<IProjectSettingsStore>();
        var row = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);
        row.Apply(
            minIdle: 0,
            maxConcurrent: 4,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: 100,
            hardBudgetUsdMicros: 200,
            now: DateTimeOffset.UtcNow);
        _ = settings.GetCached(projectId).Returns(row);

        var caps = await new ProjectBudgetSettingsAdapter(settings).GetAsync(
            projectId,
            TestContext.Current.CancellationToken);

        caps.SoftLimitUsdMicros.ShouldBe(100);
        caps.HardLimitUsdMicros.ShouldBe(200);
        await settings.DidNotReceive().FindAsync(projectId, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given no cache and a DB row, when GetAsync, then FindAsync caps are returned")]
    public async Task FallBackToFindAsync()
    {
        var projectId = ProjectId.New();
        var settings = Substitute.For<IProjectSettingsStore>();
        var row = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);
        row.Apply(
            minIdle: 0,
            maxConcurrent: 4,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: 5,
            hardBudgetUsdMicros: null,
            now: DateTimeOffset.UtcNow);
        _ = settings.GetCached(projectId).Returns((ProjectSettings?)null);
        _ = settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns(row);

        var caps = await new ProjectBudgetSettingsAdapter(settings).GetAsync(
            projectId,
            TestContext.Current.CancellationToken);

        caps.SoftLimitUsdMicros.ShouldBe(5);
        caps.HardLimitUsdMicros.ShouldBeNull();
    }

    [Fact(DisplayName = "Given no settings row, when GetAsync, then both caps are null")]
    public async Task ReturnUnlimitedWhenMissingAsync()
    {
        var projectId = ProjectId.New();
        var settings = Substitute.For<IProjectSettingsStore>();
        _ = settings.GetCached(projectId).Returns((ProjectSettings?)null);
        _ = settings.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns((ProjectSettings?)null);

        var caps = await new ProjectBudgetSettingsAdapter(settings).GetAsync(
            projectId,
            TestContext.Current.CancellationToken);

        caps.SoftLimitUsdMicros.ShouldBeNull();
        caps.HardLimitUsdMicros.ShouldBeNull();
    }
}
