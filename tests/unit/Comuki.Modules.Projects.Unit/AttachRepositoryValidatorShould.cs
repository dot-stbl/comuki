using Comuki.Modules.Projects.Application.Attachments;
using Comuki.Modules.Projects.Domain.Attachments;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Structural validation of <see cref="AttachRepositoryCommand"/>: the role
/// is normalized-on-store (trim + lower-case kebab-case); the access must
/// not be <see cref="AttachmentAccess.Unspecified"/>; the credential
/// override ref is bounded. Semantic checks (project exists, attachment
/// pair unique) live in the handler.
/// </summary>
public sealed class AttachRepositoryValidatorShould
{
    private readonly AttachRepositoryValidator validator = new();

    private static AttachRepositoryCommand WellFormedCommand()
    {
        return new AttachRepositoryCommand(
            ProjectId.New(),
            RepositoryId.New(),
            "primary",
            AttachmentAccess.Write,
            "integration-a");
    }

    [Fact(DisplayName = "Given a well-formed command, when validated, then it passes")]
    public void AcceptWellFormedCommand()
    {
        var result = validator.Validate(WellFormedCommand());

        result.IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a malformed role, when validated, then it fails")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Primary")]
    [InlineData("primary ")]
    [InlineData("primary-sub role")]
    [InlineData("primary--sub")]
    [InlineData("-primary")]
    [InlineData("primary-")]
    public void RefuseMalformedRole(string role)
    {
        var command = new AttachRepositoryCommand(
            ProjectId.New(),
            RepositoryId.New(),
            role,
            AttachmentAccess.Write,
            null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Role");
    }

    [Fact(DisplayName = "Given a role over MaxLength, when validated, then it fails")]
    public void RefuseOverlongRole()
    {
        var oversizedRole = new string('a', AttachmentRole.MaxLength + 1);

        var result = validator.Validate(
            new AttachRepositoryCommand(
                ProjectId.New(),
                RepositoryId.New(),
                oversizedRole,
                AttachmentAccess.Write,
                null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Role");
    }

    [Fact(DisplayName = "Given Unspecified access, when validated, then it fails")]
    public void RefuseUnspecifiedAccess()
    {
        var result = validator.Validate(
            new AttachRepositoryCommand(
                ProjectId.New(),
                RepositoryId.New(),
                "primary",
                AttachmentAccess.Unspecified,
                null));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "Access");
    }

    [Fact(DisplayName = "Given a credential override ref over MaxLength, when validated, then it fails")]
    public void RefuseOverlongCredentialOverrideRef()
    {
        var oversizedRef = new string('x', ProjectRepositoryAttachment.CredentialOverrideRefMaxLength + 1);

        var result = validator.Validate(
            new AttachRepositoryCommand(
                ProjectId.New(),
                RepositoryId.New(),
                "primary",
                AttachmentAccess.Write,
                oversizedRef));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(static failure => failure.PropertyName == "CredentialOverrideRef");
    }

    [Fact(DisplayName = "Given a null credential override ref, when validated, then it passes")]
    public void AcceptNullCredentialOverrideRef()
    {
        var command = new AttachRepositoryCommand(
            ProjectId.New(),
            RepositoryId.New(),
            "primary",
            AttachmentAccess.Write,
            null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }
}
