using Comuki.Modules.Projects.Domain.DomainTypes;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Truth table of <see cref="DomainTypeAdmission"/> — the pure half of the
/// domain-user intake gate. Two orthogonal switches: an allow-list of
/// intake sources (empty = any) and hard-block reason codes. Keys are
/// normalized on write so a policy authored as <c>"  GitHub "</c> answers a
/// run arriving as <c>"github"</c>.
/// </summary>
public sealed class DomainTypeAdmissionShould
{
    private static readonly DateTimeOffset now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given mixed-case padded keys, when Create is called, then the domain type and sources are normalized")]
    public void NormalizeKeysOnCreate()
    {
        var admission = DomainTypeAdmission.Create(
            ProjectId.New(),
            "  Code ",
            ["GitHub", "  gitlab", "github", "   ", "Native"],
            [],
            now);

        admission.DomainType.ShouldBe("code");
        admission.AllowedSources.ShouldBe(["github", "gitlab", "native"]);
        admission.DeniedReasons.ShouldBeEmpty();
        admission.Enabled.ShouldBeTrue();
        admission.CreatedAt.ShouldBe(now);
        admission.UpdatedAt.ShouldBe(now);
        admission.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Theory(DisplayName = "Given a policy with no allow-list, when denials are evaluated, then any source is admitted")]
    [InlineData("github")]
    [InlineData("gitlab")]
    [InlineData("native")]
    public void AdmitAnySourceWithoutAllowList(string source)
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "code", [], [], now);

        admission.EvaluateDenials(source).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given an allow-list, when the source is listed, then it is admitted regardless of casing")]
    public void AdmitListedSource()
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "code", ["github", "native"], [], now);

        admission.EvaluateDenials("GitHub").ShouldBeEmpty();
        admission.EvaluateDenials(" native ").ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given an allow-list, when the source is not listed, then it is denied as source_not_allowed")]
    public void DenyUnlistedSource()
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "code", ["github"], [], now);

        admission.EvaluateDenials("jira").ShouldBe([DomainTypeAdmission.SourceNotAllowedReason]);
    }

    [Fact(DisplayName = "Given blocking reasons, when denials are evaluated, then they win over an allowed source")]
    public void ReportBlockingReasonsOverAllowedSource()
    {
        var admission = DomainTypeAdmission.Create(
            ProjectId.New(),
            "infra",
            ["github"],
            ["needs_human_review", "budget_frozen"],
            now);

        admission.EvaluateDenials("github").ShouldBe(["needs_human_review", "budget_frozen"]);
    }

    [Fact(DisplayName = "Given a disabled policy, when denials are evaluated, then everything is denied as admission_disabled")]
    public void DenyEverythingWhenDisabled()
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "code", [], [], now);
        admission.Update(allowedSources: null, deniedReasons: null, enabled: false, now.AddMinutes(1));

        admission.Enabled.ShouldBeFalse();
        admission.EvaluateDenials("github").ShouldBe([DomainTypeAdmission.DisabledReason]);
    }

    [Fact(DisplayName = "Given a stored policy, when Update passes nulls, then the stored lists survive and only the stamp moves")]
    public void KeepStoredListsOnPartialUpdate()
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "data", ["github"], ["needs_human_review"], now);
        var later = now.AddHours(3);

        admission.Update(allowedSources: null, deniedReasons: null, enabled: null, later);

        admission.AllowedSources.ShouldBe(["github"]);
        admission.DeniedReasons.ShouldBe(["needs_human_review"]);
        admission.Enabled.ShouldBeTrue();
        admission.UpdatedAt.ShouldBe(later);
        admission.CreatedAt.ShouldBe(now);
    }

    [Fact(DisplayName = "Given a blocked policy, when Update clears the reasons, then the domain type is admitted again")]
    public void UnblockOnClearedReasons()
    {
        var admission = DomainTypeAdmission.Create(ProjectId.New(), "data", [], ["needs_human_review"], now);

        admission.Update(allowedSources: null, deniedReasons: [], enabled: null, now.AddMinutes(5));

        admission.DeniedReasons.ShouldBeEmpty();
        admission.EvaluateDenials("github").ShouldBeEmpty();
    }

    [Theory(DisplayName = "Given any key spelling, when NormalizeKey is called, then it is trimmed and lower-cased")]
    [InlineData(" Code ", "code")]
    [InlineData("GITHUB", "github")]
    [InlineData("native", "native")]
    [InlineData("   ", "")]
    public void NormalizeSingleKey(string key, string expected)
    {
        DomainTypeAdmission.NormalizeKey(key).ShouldBe(expected);
    }
}
