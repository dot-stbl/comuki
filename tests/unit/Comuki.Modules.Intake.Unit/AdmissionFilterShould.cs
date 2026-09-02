using Comuki.Modules.Intake.Application.Admission;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>AdmissionFilter parse tolerance: unknown fields ignored, malformed json degrades to match-everything.</summary>
public sealed class AdmissionFilterShould
{
    [Fact(DisplayName = "Given a well-formed filter, when parsed, then both lists come through")]
    public void ParseWellFormed()
    {
        var filter = AdmissionFilter.Parse(/*lang=json,strict*/ """{"labelsAny": ["bug"], "projects": ["COMUKI"], "unknown": 42}""");

        filter.LabelsAny.Count.ShouldBe(1);
        filter.LabelsAny.Contains("bug").ShouldBeTrue();
        filter.Projects.Count.ShouldBe(1);
        filter.Projects.Contains("COMUKI").ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a degenerate filter, when parsed, then it degrades to match-everything")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData(/*lang=json,strict*/ """{"labelsAny": null, "projects": "oops"}""")]
    public void DegradeToAny(string? json)
    {
        var filter = AdmissionFilter.Parse(json);

        filter.LabelsAny.ShouldBeEmpty();
        filter.Projects.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a filter with empty-string entries, when parsed, then they are dropped")]
    public void DropEmptyEntries()
    {
        var filter = AdmissionFilter.Parse(/*lang=json,strict*/ """{"labelsAny": ["", "bug"]}""");

        filter.LabelsAny.Count.ShouldBe(1);
    }
}
