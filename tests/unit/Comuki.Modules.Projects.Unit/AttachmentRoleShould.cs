using Comuki.Modules.Projects.Domain.Attachments;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Open-set <see cref="AttachmentRole"/>: well-known statics for the
/// spec-defined defaults plus a normalized string passthrough for
/// extensions. The role is the row's meaning; "no role" is the only thing
/// that throws on parse.
/// </summary>
public sealed class AttachmentRoleShould
{
    [Fact(DisplayName = "Given a padded role, when Normalize is called, then it is trimmed and lower-cased")]
    public void NormalizeTrimsAndLowers()
    {
        AttachmentRole.Normalize(" Primary ").ShouldBe("primary");
    }

    [Fact(DisplayName = "Given an upper-case role, when Normalize is called, then it is lower-cased")]
    public void NormalizeLowersUpperCase()
    {
        AttachmentRole.Normalize("LIBRARY").ShouldBe("library");
    }

    [Fact(DisplayName = "Given a hyphenated role, when Normalize is called, then dashes are preserved")]
    public void NormalizePreservesDashes()
    {
        AttachmentRole.Normalize("deploy-gitops").ShouldBe("deploy-gitops");
    }

    [Fact(DisplayName = "Given whitespace, when Normalize is called, then it is empty")]
    public void NormalizeWhitespaceIsEmpty()
    {
        AttachmentRole.Normalize("   ").ShouldBe("");
    }

    [Fact(DisplayName = "Given the primary wire form, when FromWire is called, then Primary is returned")]
    public void ParsePrimary()
    {
        AttachmentRole.FromWire("primary").ShouldBe(AttachmentRole.Primary);
    }

    [Fact(DisplayName = "Given the service wire form, when FromWire is called, then Service is returned")]
    public void ParseService()
    {
        AttachmentRole.FromWire("service").ShouldBe(AttachmentRole.Service);
    }

    [Fact(DisplayName = "Given the frontend wire form, when FromWire is called, then Frontend is returned")]
    public void ParseFrontend()
    {
        AttachmentRole.FromWire("frontend").ShouldBe(AttachmentRole.Frontend);
    }

    [Fact(DisplayName = "Given the library wire form, when FromWire is called, then Library is returned")]
    public void ParseLibrary()
    {
        AttachmentRole.FromWire("library").ShouldBe(AttachmentRole.Library);
    }

    [Fact(DisplayName = "Given the deploy-gitops wire form, when FromWire is called, then DeployGitOps is returned")]
    public void ParseDeployGitOps()
    {
        AttachmentRole.FromWire("deploy-gitops").ShouldBe(AttachmentRole.DeployGitOps);
    }

    [Fact(DisplayName = "Given the docs wire form, when FromWire is called, then Docs is returned")]
    public void ParseDocs()
    {
        AttachmentRole.FromWire("docs").ShouldBe(AttachmentRole.Docs);
    }

    [Fact(DisplayName = "Given an unknown non-empty role, when FromWire is called, then it is stored as an extension role (open set)")]
    public void AcceptUnknownRoleAsExtension()
    {
        var role = AttachmentRole.FromWire("data-feed");

        role.Value.ShouldBe("data-feed");
        role.ShouldNotBeOneOf(
            AttachmentRole.Primary,
            AttachmentRole.Service,
            AttachmentRole.Frontend,
            AttachmentRole.Library,
            AttachmentRole.DeployGitOps,
            AttachmentRole.Docs);
    }

    [Fact(DisplayName = "Given an unknown role with mixed casing/padding, when FromWire is called, then it is normalized and stored as an extension")]
    public void AcceptUnknownRoleAfterNormalization()
    {
        var role = AttachmentRole.FromWire("  CUSTOM-build  ");

        role.Value.ShouldBe("custom-build");
    }

    [Fact(DisplayName = "Given empty wire, when FromWire is called, then it throws ArgumentException")]
    public void RejectEmptyRole()
    {
        Should.Throw<ArgumentException>(static () => AttachmentRole.FromWire(""));
    }

    [Fact(DisplayName = "Given whitespace wire, when FromWire is called, then it throws ArgumentException")]
    public void RejectWhitespaceRole()
    {
        Should.Throw<ArgumentException>(static () => AttachmentRole.FromWire("   "));
    }
}
