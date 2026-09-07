using Comuki.Modules.Projects.Application.Settings.Update;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Structural validation of <see cref="UpdateSettingsCommand"/>: scale
/// ranges, the min_idle ≤ max_concurrent invariant, the idle TTL bounds
/// and the new domain-type routing fields (mode + JSON map shape).
/// </summary>
public sealed class UpdateSettingsValidatorShould
{
    private readonly UpdateSettingsValidator validator = new();

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
            ProjectDomainType.Standard, null);

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
            ProjectDomainType.Standard, null);

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
            ProjectDomainType.Standard, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "IdleTtlSeconds");
    }

    [Fact(DisplayName = "Given a null idle TTL (engine default), when validated, then it passes")]
    public void AcceptNullIdleTtl()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            ProjectDomainType.Standard, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given Custom mode without a JSON map, when validated, then it fails")]
    public void RefuseCustomWithoutJson()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            ProjectDomainType.Custom, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "CustomDomainTypesJson");
    }

    [Fact(DisplayName = "Given Custom mode with a malformed JSON map, when validated, then it fails")]
    public void RefuseMalformedCustomJson()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            ProjectDomainType.Custom, "{not-json");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "CustomDomainTypesJson");
    }

    [Fact(DisplayName = "Given Custom mode with a JSON map whose value is empty, when validated, then it fails")]
    public void RefuseCustomJsonWithEmptyValue()
    {
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            ProjectDomainType.Custom, /*lang=json,strict*/ """{"code": ""}""");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "CustomDomainTypesJson");
    }

    [Fact(DisplayName = "Given Hybrid mode with a JSON map and an unknown domain, when validated, then it passes")]
    public void AcceptHybridWithAnyJsonShape()
    {
        // The validator only checks shape; semantic "unknown domain" is the
        // resolver's job (it falls back to the default for Hybrid projects).
        var command = new UpdateSettingsCommand(ProjectId.New(), 1, 0, 4, null, false, false, false, false, null, null,
            ProjectDomainType.Hybrid, /*lang=json,strict*/ """{"code": "implement"}""");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
