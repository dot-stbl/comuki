using Comuki.Modules.Projects.Application.Admission;
using Comuki.Modules.Projects.Application.DomainTypes;
using Comuki.Modules.Projects.Application.Ports;
using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Behaviour of <see cref="DomainTypeAdmissionService"/> — admission first,
/// routing second:
/// <list type="bullet">
///     <item>a project with no policy for the domain type falls back to its
///     declared routing mode (Custom closed, Standard/Hybrid open);</item>
///     <item>a policy that denies short-circuits before routing, so the
///     decision carries the policy's reason codes and no profile;</item>
///     <item>an admitted domain type is routed by the real resolver, whose
///     typed "not mapped" failure becomes a <c>profile_*</c> reason instead
///     of an exception escaping to the caller.</item>
/// </list>
/// </summary>
public sealed class DomainTypeAdmissionServiceShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given a Standard project with no policy, when evaluated, then it is admitted with the default profile")]
    public async Task AdmitStandardProjectWithoutPolicyAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(projectId, ProjectDomainType.Standard, null);
        var service = AdmissionFixtures.Service(projectId, settings, policy: null);

        var decision = await service.EvaluateAsync(projectId, "Code", "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeTrue();
        decision.DomainType.ShouldBe("code");
        decision.ProfileKey.ShouldBe(ProjectSettings.DefaultDomainProfileKey);
        decision.Reasons.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a Custom project with no policy, when evaluated, then it is denied as unregistered_domain_type")]
    public async Task DenyCustomProjectWithoutPolicyAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(
            projectId,
            ProjectDomainType.Custom,
            /*lang=json,strict*/ """{"code":"docs-writer"}""");
        var service = AdmissionFixtures.Service(projectId, settings, policy: null);

        var decision = await service.EvaluateAsync(projectId, "code", "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.ProfileKey.ShouldBeNull();
        decision.Reasons.ShouldBe([DomainTypeAdmissionService.UnregisteredReason]);
    }

    [Fact(DisplayName = "Given a Custom project with a policy and a mapped domain, when evaluated, then the mapped profile is returned")]
    public async Task AdmitCustomProjectWithMappedProfileAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(
            projectId,
            ProjectDomainType.Custom,
            /*lang=json,strict*/ """{"code":"docs-writer"}""");
        var policy = DomainTypeAdmission.Create(projectId, "code", [], [], now);
        var service = AdmissionFixtures.Service(projectId, settings, policy);

        var decision = await service.EvaluateAsync(projectId, "code", "native", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeTrue();
        decision.ProfileKey.ShouldBe("docs-writer");
    }

    [Fact(DisplayName = "Given a Custom project whose map misses the admitted domain, when evaluated, then it is denied as profile_missing")]
    public async Task DenyCustomProjectWithUnmappedProfileAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(
            projectId,
            ProjectDomainType.Custom,
            /*lang=json,strict*/ """{"code":"docs-writer"}""");
        var policy = DomainTypeAdmission.Create(projectId, "data", [], [], now);
        var service = AdmissionFixtures.Service(projectId, settings, policy);

        var decision = await service.EvaluateAsync(projectId, "data", "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.Reasons.ShouldBe([$"{DomainTypeAdmissionService.ProfileReasonPrefix}missing"]);
    }

    [Fact(DisplayName = "Given a policy with blocking reasons, when evaluated, then the decision carries them and no profile")]
    public async Task DenyBlockedDomainTypeAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(projectId, ProjectDomainType.Standard, null);
        var policy = DomainTypeAdmission.Create(projectId, "infra", [], ["needs_human_review"], now);
        var service = AdmissionFixtures.Service(projectId, settings, policy);

        var decision = await service.EvaluateAsync(projectId, "infra", "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.ProfileKey.ShouldBeNull();
        decision.Reasons.ShouldBe(["needs_human_review"]);
    }

    [Fact(DisplayName = "Given a policy with an allow-list, when the source is outside it, then it is denied as source_not_allowed")]
    public async Task DenySourceOutsideAllowListAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(projectId, ProjectDomainType.Standard, null);
        var policy = DomainTypeAdmission.Create(projectId, "code", ["native"], [], now);
        var service = AdmissionFixtures.Service(projectId, settings, policy);

        var decision = await service.EvaluateAsync(projectId, "code", "GitHub", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.Reasons.ShouldBe([DomainTypeAdmission.SourceNotAllowedReason]);
    }

    [Fact(DisplayName = "Given a policy with an allow-list, when the source is inside it, then it is admitted")]
    public async Task AdmitSourceInsideAllowListAsync()
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(projectId, ProjectDomainType.Standard, null);
        var policy = DomainTypeAdmission.Create(projectId, "code", ["github", "native"], [], now);
        var service = AdmissionFixtures.Service(projectId, settings, policy);

        var decision = await service.EvaluateAsync(projectId, "code", " GITHUB ", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeTrue();
        decision.ProfileKey.ShouldBe(ProjectSettings.DefaultDomainProfileKey);
    }

    [Fact(DisplayName = "Given a project without a settings row, when evaluated, then it is denied as project_settings_missing")]
    public async Task DenyProjectWithoutSettingsAsync()
    {
        var projectId = ProjectId.New();
        var service = AdmissionFixtures.Service(projectId, settings: null, policy: null);

        var decision = await service.EvaluateAsync(projectId, "code", "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.Reasons.ShouldBe([DomainTypeAdmissionService.SettingsMissingReason]);
    }

    [Theory(DisplayName = "Given a blank domain type, when evaluated, then it is denied as empty_domain_type")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DenyBlankDomainTypeAsync(string domainType)
    {
        var projectId = ProjectId.New();
        var settings = AdmissionFixtures.Settings(projectId, ProjectDomainType.Standard, null);
        var service = AdmissionFixtures.Service(projectId, settings, policy: null);

        var decision = await service.EvaluateAsync(projectId, domainType, "github", TestContext.Current.CancellationToken);

        decision.Admitted.ShouldBeFalse();
        decision.Reasons.ShouldBe([DomainTypeAdmissionService.EmptyDomainTypeReason]);
    }
}

/// <summary>
/// Arrange helpers: a settings row in a chosen routing mode and a service
/// wired over substituted stores plus the real resolver (routing is pure —
/// substituting it would test the substitute).
/// </summary>
file static class AdmissionFixtures
{
    private static readonly DateTimeOffset created = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Settings row carrying a routing mode and an optional JSON map.</summary>
    /// <param name="projectId"></param>
    /// <param name="mode"></param>
    /// <param name="customDomainTypesJson"></param>
    /// <returns></returns>
    public static ProjectSettings Settings(
        ProjectId projectId,
        ProjectDomainType mode,
        string? customDomainTypesJson)
    {
        var settings = ProjectSettings.CreateDefaults(projectId, created);
        settings.Apply(
            minIdle: 0,
            maxConcurrent: ProjectSettings.DefaultMaxConcurrent,
            idleTtlSeconds: null,
            approveRequired: false,
            knowledgeEnabled: false,
            verifyEnabled: false,
            proxyEnabled: false,
            softBudgetUsdMicros: null,
            hardBudgetUsdMicros: null,
            domainType: mode,
            customDomainTypesJson: customDomainTypesJson,
            now: created.AddMinutes(1));

        return settings;
    }

    /// <summary>Service over substituted stores; the resolver is the real one.</summary>
    /// <param name="projectId"></param>
    /// <param name="settings"></param>
    /// <param name="policy"></param>
    /// <returns></returns>
    public static DomainTypeAdmissionService Service(
        ProjectId projectId,
        ProjectSettings? settings,
        DomainTypeAdmission? policy)
    {
        var settingsStore = Substitute.For<IProjectSettingsStore>();
        settingsStore.FindAsync(projectId, Arg.Any<CancellationToken>()).Returns(settings);

        var admissionStore = Substitute.For<IDomainTypeAdmissionStore>();
        admissionStore
            .FindAsync(projectId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(policy);

        return new DomainTypeAdmissionService(settingsStore, admissionStore, new ProjectDomainTypeResolver());
    }
}
