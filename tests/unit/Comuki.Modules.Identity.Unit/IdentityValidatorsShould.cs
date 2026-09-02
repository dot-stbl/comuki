using Comuki.Modules.Identity.Application.ApiKeys.Issue;
using Comuki.Modules.Identity.Application.Assignments.Grant;
using Comuki.Modules.Identity.Application.Sessions;
using Comuki.Modules.Identity.Application.Users;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Modules.Identity.Domain.Roles;
using Comuki.Modules.Identity.Domain.Scopes;
using Comuki.Modules.Identity.Domain.Subjects;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>Structural FluentValidation rules for Identity commands.</summary>
public sealed class IdentityValidatorsShould
{
    [Fact(DisplayName = "Given a valid CreateUserCommand, when validated, then it passes")]
    public void AcceptValidCreateUser()
    {
        new CreateUserValidator()
            .Validate(new CreateUserCommand("user@example.com", "Ada", "password1"))
            .IsValid.ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a broken CreateUserCommand field, when validated, then it fails")]
    [InlineData("", "Ada", "password1")]
    [InlineData("not-an-email", "Ada", "password1")]
    [InlineData("user@example.com", "", "password1")]
    [InlineData("user@example.com", "Ada", "short")]
    public void RefuseInvalidCreateUser(string email, string displayName, string password)
    {
        new CreateUserValidator()
            .Validate(new CreateUserCommand(email, displayName, password))
            .IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a valid LoginCommand, when validated, then it passes")]
    public void AcceptValidLogin()
    {
        new LoginValidator().Validate(new LoginCommand("user@example.com", "secret")).IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty LoginCommand password, when validated, then it fails")]
    public void RefuseEmptyLoginPassword()
    {
        new LoginValidator().Validate(new LoginCommand("user@example.com", "")).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a project scope without project id, when GrantRole is validated, then it fails")]
    public void RefuseGrantWithoutProjectId()
    {
        var command = new GrantRoleCommand(
            RoleSubject.ForUser(UserId.New()),
            Role.Member,
            new AssignmentScope(ScopeLevel.Project, null),
            ActingAs: null);

        new GrantRoleValidator().Validate(command).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a coherent GrantRoleCommand, when validated, then it passes")]
    public void AcceptValidGrant()
    {
        var command = new GrantRoleCommand(
            RoleSubject.ForUser(UserId.New()),
            Role.Member,
            AssignmentScope.ForProject(ProjectId.New()),
            ActingAs: null);

        new GrantRoleValidator().Validate(command).IsValid.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given an empty IssueApiKey name, when validated, then it fails")]
    public void RefuseEmptyApiKeyName()
    {
        new IssueApiKeyValidator().Validate(new IssueApiKeyCommand(UserId.New(), "")).IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a valid IssueApiKeyCommand, when validated, then it passes")]
    public void AcceptValidIssueApiKey()
    {
        new IssueApiKeyValidator().Validate(new IssueApiKeyCommand(UserId.New(), "ci")).IsValid.ShouldBeTrue();
    }
}
