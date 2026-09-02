using Comuki.Modules.Intake.Application.Tickets;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Structural validation of native ticket commands.</summary>
public sealed class CreateNativeTicketValidatorShould
{
    private readonly CreateNativeTicketValidator validator = new();

    [Fact(DisplayName = "Given a well-formed native ticket, when validated, then it passes")]
    public void AcceptWellFormed()
    {
        var result = validator.Validate(new CreateNativeTicketCommand(
            ProjectId.New(),
            "Ship coverage",
            "body",
            "native-1",
            "ada"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty title, when validated, then it fails")]
    public void RefuseEmptyTitle()
    {
        var result = validator.Validate(new CreateNativeTicketCommand(
            ProjectId.New(),
            "",
            "body",
            null,
            null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Title");
    }

    [Fact(DisplayName = "Given a malformed external id, when validated, then it fails")]
    public void RefuseBadExternalId()
    {
        var result = validator.Validate(new CreateNativeTicketCommand(
            ProjectId.New(),
            "Ship",
            "body",
            "bad id!",
            null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "ExternalId");
    }

    [Fact(DisplayName = "Given a null external id, when validated, then it passes (generated later)")]
    public void AcceptMissingExternalId()
    {
        validator.Validate(new CreateNativeTicketCommand(ProjectId.New(), "Ship", "body", null, null))
            .IsValid.ShouldBeTrue();
    }
}
