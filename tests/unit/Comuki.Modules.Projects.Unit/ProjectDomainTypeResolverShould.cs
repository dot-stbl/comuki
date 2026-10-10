using Comuki.Modules.Projects.Application.DomainTypes;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Behaviour of <see cref="ProjectDomainTypeResolver"/> per project mode:
/// <list type="bullet">
///     <item>Standard — every domain type returns the fixed default profile key.</item>
///     <item>Custom — only the JSON map is consulted; missing keys throw.</item>
///     <item>Hybrid — JSON map wins when present, otherwise the default; both miss throws.</item>
/// </list>
/// Malformed JSON and oversized values throw the same typed exception as a
/// missing key so the caller sees one error class, not three.
/// </summary>
public sealed class ProjectDomainTypeResolverShould
{
    private static readonly IProjectDomainTypeResolver resolver = new ProjectDomainTypeResolver();

    [Theory(DisplayName = "Given a Standard project, when Resolve is called for any domain type, then the default key is returned")]
    [InlineData("code")]
    [InlineData("data")]
    [InlineData("research")]
    [InlineData("infra")]
    public void StandardAlwaysReturnsDefault(string domainType)
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));

        var profileKey = resolver.ResolveProfileKey(settings, domainType);

        profileKey.ShouldBe(ProjectSettings.DefaultDomainProfileKey);
    }

    [Fact(DisplayName = "Given a Standard project with a JSON map, when Resolve is called, then the JSON is ignored")]
    public void StandardIgnoresJsonMap()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
        // The JSON map is set on the entity but Standard mode skips it.
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
            customDomainTypesJson: /*lang=json,strict*/ """{"code":"docs-writer"}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        resolver.ResolveProfileKey(settings, "code").ShouldBe(ProjectSettings.DefaultDomainProfileKey);
    }

    [Fact(DisplayName = "Given a Custom project whose JSON has the domain, when Resolve is called, then the mapped profile is returned")]
    public void CustomReturnsMappedProfile()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            customDomainTypesJson: /*lang=json,strict*/ """{"code":"docs-writer","data":"data-pipeline"}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        resolver.ResolveProfileKey(settings, "code").ShouldBe("docs-writer");
        resolver.ResolveProfileKey(settings, "data").ShouldBe("data-pipeline");
    }

    [Fact(DisplayName = "Given a Custom project whose JSON misses the domain, when Resolve is called, then it throws missing")]
    public void CustomThrowsOnMissingDomain()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            customDomainTypesJson: /*lang=json,strict*/ """{"code":"docs-writer"}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        var exception = Should.Throw<ProjectDomainTypeNotMappedException>(
            () => resolver.ResolveProfileKey(settings, "data"));
        exception.DomainType.ShouldBe("data");
        exception.ProjectDomainType.ShouldBe(nameof(ProjectDomainType.Custom));
        exception.Reason.ShouldBe("missing");
    }

    [Fact(DisplayName = "Given a Custom project with an empty JSON, when Resolve is called, then it throws empty")]
    public void CustomThrowsOnEmptyJson()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            customDomainTypesJson: "  ",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        var exception = Should.Throw<ProjectDomainTypeNotMappedException>(
            () => resolver.ResolveProfileKey(settings, "code"));
        exception.Reason.ShouldBe("empty");
    }

    [Fact(DisplayName = "Given a Custom project with malformed JSON, when Resolve is called, then it throws malformed")]
    public void CustomThrowsOnMalformedJson()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            customDomainTypesJson: """{"code":}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        var exception = Should.Throw<ProjectDomainTypeNotMappedException>(
            () => resolver.ResolveProfileKey(settings, "code"));
        exception.Reason.ShouldBe("malformed");
    }

    [Fact(DisplayName = "Given a Hybrid project whose JSON has the domain, when Resolve is called, then the mapped profile wins")]
    public void HybridReturnsMappedProfileOverDefault()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            domainType: ProjectDomainType.Hybrid,
            customDomainTypesJson: /*lang=json,strict*/ """{"data":"data-pipeline"}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        resolver.ResolveProfileKey(settings, "data").ShouldBe("data-pipeline");
    }

    [Fact(DisplayName = "Given a Hybrid project whose JSON misses the domain, when Resolve is called, then the default is returned")]
    public void HybridFallsBackToDefault()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
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
            domainType: ProjectDomainType.Hybrid,
            customDomainTypesJson: /*lang=json,strict*/ """{"data":"data-pipeline"}""",
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        // code is not in the map → fallback to the default.
        resolver.ResolveProfileKey(settings, "code").ShouldBe(ProjectSettings.DefaultDomainProfileKey);
    }

    [Fact(DisplayName = "Given a Hybrid project with no JSON map, when Resolve is called, then the default is returned")]
    public void HybridWithoutJsonReturnsDefault()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));
        // CreateDefaults leaves CustomDomainTypesJson null; Hybrid mode treats null as "no map, fall through".
        settings.DomainType.ShouldBe(ProjectDomainType.Standard);

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
            domainType: ProjectDomainType.Hybrid,
            customDomainTypesJson: null,
            now: new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero));

        resolver.ResolveProfileKey(settings, "anything").ShouldBe(ProjectSettings.DefaultDomainProfileKey);
    }

    [Fact(DisplayName = "Given a settings row, when Resolve is called with an empty domain type, then ArgumentException is thrown")]
    public void ResolveRejectsEmptyDomainType()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero));

        Should.Throw<ArgumentException>(() => resolver.ResolveProfileKey(settings, "  "));
    }
}

