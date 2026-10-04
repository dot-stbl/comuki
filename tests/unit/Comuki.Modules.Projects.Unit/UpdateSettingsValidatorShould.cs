using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Structural validation of <see cref="UpdateSettingsCommand"/>: scale
/// ranges, the min_idle ≤ max_concurrent invariant, the idle TTL bounds,
/// and the <c>customDomainTypesJson</c> map shape (length, JSON object of
/// non-empty strings, required when <see cref="ProjectDomainType.Custom"/>).
/// </summary>
public sealed class UpdateSettingsValidatorShould
{
    private readonly UpdateSettingsValidator validator = new();

    private static UpdateSettingsCommand BuildCommand(ProjectDomainType domainType, string? customDomainTypesJson)
    {
        return new UpdateSettingsCommand(ProjectId.New(), Version: 1, MinIdle: 0, MaxConcurrent: 4,
            IdleTtlSeconds: null, ApproveRequired: false, KnowledgeEnabled: false, VerifyEnabled: false,
            ProxyEnabled: false, SoftBudgetUsdMicros: null, HardBudgetUsdMicros: null,
            DomainType: domainType, CustomDomainTypesJson: customDomainTypesJson);
    }

    [Fact(DisplayName = "Given a well-formed command, when validated, then it passes")]
    public void AcceptWellFormedCommand()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), Version: 3, MinIdle: 1, MaxConcurrent: 8,
            IdleTtlSeconds: 900, ApproveRequired: true, KnowledgeEnabled: false, VerifyEnabled: true,
            ProxyEnabled: false, SoftBudgetUsdMicros: 1_000_000, HardBudgetUsdMicros: 5_000_000,
            DomainType: ProjectDomainType.Standard, CustomDomainTypesJson: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a version below one, when validated, then it fails")]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefuseNonPositiveVersion(int version)
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), version, 0, 4, null, false, false, false, false, null, null,
            DomainType: ProjectDomainType.Standard, CustomDomainTypesJson: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Version");
    }

    [Fact(DisplayName = "Given min_idle above max_concurrent, when validated, then it fails")]
    public void RefuseIdleFloorAboveCap()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, MinIdle: 5, MaxConcurrent: 4,
            IdleTtlSeconds: null, ApproveRequired: false, KnowledgeEnabled: false, VerifyEnabled: false,
            ProxyEnabled: false, SoftBudgetUsdMicros: null, HardBudgetUsdMicros: null,
            DomainType: ProjectDomainType.Standard, CustomDomainTypesJson: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.ErrorMessage.Contains("min_idle"));
    }

    [Theory(DisplayName = "Given an out-of-bounds idle TTL, when validated, then it fails")]
    [InlineData(29)]
    [InlineData(86401)]
    public void RefuseOutOfBoundIdleTtl(int idleTtlSeconds)
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, idleTtlSeconds, false, false, false, false, null, null,
            DomainType: ProjectDomainType.Standard, CustomDomainTypesJson: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "IdleTtlSeconds");
    }

    [Fact(DisplayName = "Given a null idle TTL (engine default), when validated, then it passes")]
    public void AcceptNullIdleTtl()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            DomainType: ProjectDomainType.Standard, CustomDomainTypesJson: null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a Custom command with a well-formed JSON map, when validated, then it passes")]
    public void AcceptCustomWithValidJson()
    {
        var command = BuildCommand(ProjectDomainType.Custom, """{"code":"implement"}""");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a Hybrid command with a well-formed JSON map, when validated, then it passes")]
    public void AcceptHybridWithValidJson()
    {
        var command = BuildCommand(ProjectDomainType.Hybrid, """{"code":"implement"}""");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a Hybrid command with no JSON map, when validated, then it passes")]
    public void AcceptHybridWithoutJson()
    {
        var command = BuildCommand(ProjectDomainType.Hybrid, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a Custom command with absent/blank JSON, when validated, then it fails as required")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefuseAbsentJsonForCustom(string? json)
    {
        var command = BuildCommand(ProjectDomainType.Custom, json);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure =>
            failure.PropertyName == "CustomDomainTypesJson" && failure.ErrorMessage.Contains("required"));
    }

    [Theory(DisplayName = "Given a JSON map over the length cap, when validated, then it fails on the cap")]
    [InlineData(ProjectDomainType.Custom)]
    [InlineData(ProjectDomainType.Hybrid)]
    public void RefuseOversizedJson(ProjectDomainType domainType)
    {
        var command = BuildCommand(domainType, new string('x', UpdateSettingsValidator.MaxCustomDomainTypesJsonLength + 1));

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.ErrorMessage.Contains("8192"));
    }

    [Theory(DisplayName = "Given a malformed JSON map, when validated, then it fails on the shape rule")]
    [InlineData(ProjectDomainType.Custom)]
    [InlineData(ProjectDomainType.Hybrid)]
    public void RefuseMalformedJson(ProjectDomainType domainType)
    {
        var command = BuildCommand(domainType, """{"code":}""");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.ErrorMessage.Contains("JSON object"));
    }
}
