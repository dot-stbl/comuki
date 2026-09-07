using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Domain-default and <see cref="ProjectSettings.Apply"/> contract for the
/// domain-routing fields added in issue #11 slice 1:
/// <see cref="ProjectSettings.DomainType"/> and
/// <see cref="ProjectSettings.CustomDomainTypesJson"/>.
/// </summary>
public sealed class ProjectDomainRoutingSettingsShould
{
    private readonly DateTimeOffset now = new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given defaults, when CreateDefaults runs, then DomainType is Standard and CustomDomainTypesJson is null")]
    public void DefaultRoutingIsStandardWithNullJson()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), now);

        settings.DomainType.ShouldBe(ProjectDomainType.Standard);
        settings.CustomDomainTypesJson.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a settings row, when Apply mutates the routing fields, then they are written and the version bumps")]
    public void ApplyMutatesRoutingFields()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), now);
        var later = now.AddMinutes(7);

        settings.Apply(
            minIdle: 1,
            maxConcurrent: 8,
            idleTtlSeconds: 600,
            approveRequired: true,
            knowledgeEnabled: true,
            verifyEnabled: false,
            proxyEnabled: true,
            softBudgetUsdMicros: 1_000_000,
            hardBudgetUsdMicros: 5_000_000,
            domainType: ProjectDomainType.Hybrid,
            customDomainTypesJson: /*lang=json,strict*/ """{"code":"implement","data":"data-pipeline"}""",
            now: later);

        settings.DomainType.ShouldBe(ProjectDomainType.Hybrid);
        settings.CustomDomainTypesJson.ShouldBe(/*lang=json,strict*/ """{"code":"implement","data":"data-pipeline"}""");
        settings.Version.ShouldBe(2);
        settings.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given a Custom project with a JSON map, when Apply clears the JSON, then the JSON is null and the version bumps")]
    public void ApplyClearsCustomDomainTypesJson()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), now);
        settings.Apply(
            minIdle: 0,
            maxConcurrent: 4,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: null,
            hardBudgetUsdMicros: null,
            domainType: ProjectDomainType.Custom,
            customDomainTypesJson: /*lang=json,strict*/ """{"code":"implement"}""",
            now: now.AddMinutes(1));

        settings.DomainType.ShouldBe(ProjectDomainType.Custom);
        settings.CustomDomainTypesJson.ShouldBe(/*lang=json,strict*/ """{"code":"implement"}""");
        settings.Version.ShouldBe(2);

        settings.Apply(
            minIdle: 0,
            maxConcurrent: 4,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: null,
            hardBudgetUsdMicros: null,
            domainType: ProjectDomainType.Standard,
            customDomainTypesJson: null,
            now: now.AddMinutes(2));

        settings.DomainType.ShouldBe(ProjectDomainType.Standard);
        settings.CustomDomainTypesJson.ShouldBeNull();
        settings.Version.ShouldBe(3);
    }
}
