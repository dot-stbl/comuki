using Comuki.Shared.Bootstrap.Versioning;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Bootstrap.Unit.Versioning;

/// <summary>
/// Build-information parsing (issue #56): the informational version splits
/// into version + sha, the metadata keys supply the build date and mode,
/// and missing pieces degrade to Unknown / "unknown".
/// </summary>
public sealed class ComukiBuildInfoShould
{
    [Fact(DisplayName = "Given version+sha with date and mode metadata, when composed, then every field is populated")]
    public void ParseVersionShaDateAndMode()
    {
        var information = ComukiBuildInfo.Compose(
            "1.2.3+41d265deae51dbd82291ef85a376a4b123d0521c",
            "2026-09-11",
            "Debug");

        information.Version.ShouldBe("1.2.3");
        information.Revision.ShouldBe("41d265deae51dbd82291ef85a376a4b123d0521c");
        information.BuildDateUtc.ShouldBe("2026-09-11");
        information.Mode.ShouldBe("Debug");
    }

    [Fact(DisplayName = "Given an informational version without sha, when composed, then Revision is null")]
    public void RevisionIsNullWithoutShaSuffix()
    {
        var information = ComukiBuildInfo.Compose("1.2.3", "2026-09-11", "Release");

        information.Version.ShouldBe("1.2.3");
        information.Revision.ShouldBeNull();
        information.Mode.ShouldBe("Release");
    }

    [Fact(DisplayName = "Given a sha suffix but no metadata, when composed, then the mode falls back to unknown")]
    public void ModeFallsBackToUnknown()
    {
        var information = ComukiBuildInfo.Compose("0.1.0+abc", null, " ");

        information.Revision.ShouldBe("abc");
        information.BuildDateUtc.ShouldBeNull();
        information.Mode.ShouldBe("unknown");
    }

    [Theory(DisplayName = "Given a blank informational version, when composed, then the result is Unknown")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankInformationalVersionYieldsUnknown(string? informationalVersion)
    {
        var information = ComukiBuildInfo.Compose(informationalVersion, "2026-09-11", "Debug");

        information.ShouldBe(ComukiBuildInformation.Unknown);
        information.Version.ShouldBe("0.0.0");
    }

    [Fact(DisplayName = "Given an empty sha suffix, when composed, then Revision is null")]
    public void EmptyShaSuffixYieldsNullRevision()
    {
        var information = ComukiBuildInfo.Compose("1.0.0+", "2026-09-11", "Debug");

        information.Version.ShouldBe("1.0.0");
        information.Revision.ShouldBeNull();
    }

    [Fact(DisplayName = "Given full build information, when rendered, then the version line is flat key=value")]
    public void RenderFullVersionLine()
    {
        var information = new ComukiBuildInformation(
            "1.0.0",
            "41d265deae51dbd82291ef85a376a4b123d0521c",
            "2026-09-11",
            "Debug");

        information.ToVersionLine("comuki")
            .ShouldBe("comuki version=1.0.0 sha=41d265deae51dbd82291ef85a376a4b123d0521c build=2026-09-11 mode=debug");
    }

    [Fact(DisplayName = "Given Unknown build information, when rendered, then absent segments are omitted")]
    public void RenderUnknownOmitsAbsentSegments()
    {
        ComukiBuildInformation.Unknown.ToVersionLine("comuki-translator")
            .ShouldBe("comuki-translator version=0.0.0 mode=unknown");
    }
}
