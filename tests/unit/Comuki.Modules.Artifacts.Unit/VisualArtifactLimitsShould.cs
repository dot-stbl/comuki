using Comuki.Modules.Artifacts.Domain.VisualArtifacts;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Artifacts.Unit;

/// <summary>
/// Behaviour-table for <see cref="VisualArtifactLimits"/> — the
/// per-mime allow-list + size caps the publish service enforces
/// before it ever touches MinIO. One row per concern; the cap
/// values live in named constants so a regression in the cap is
/// one assertion away from being caught.
/// </summary>
public sealed class VisualArtifactLimitsShould
{
    [Theory(DisplayName = "Given an allowed MIME, when IsAllowedMime is called, then it returns true")]
    [InlineData("image/png")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("IMAGE/PNG")]
    public void AcceptsAllowedMime(string contentType)
    {
        VisualArtifactLimits.IsAllowedMime(contentType).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a disallowed MIME, when IsAllowedMime is called, then it returns false")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf")]
    [InlineData("video/mp4")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsDisallowedMime(string? contentType)
    {
        VisualArtifactLimits.IsAllowedMime(contentType).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an html payload, when MaxBytesFor is called, then it returns the 1 MiB cap")]
    public void ReturnsHtmlCap()
    {
        VisualArtifactLimits.MaxBytesFor("text/html").ShouldBe(VisualArtifactLimits.HtmlMaxBytes);
        VisualArtifactLimits.HtmlMaxBytes.ShouldBe(1L * 1024 * 1024);
    }

    [Fact(DisplayName = "Given an svg payload, when MaxBytesFor is called, then it returns the 1 MiB cap")]
    public void ReturnsSvgCap()
    {
        VisualArtifactLimits.MaxBytesFor("image/svg+xml").ShouldBe(VisualArtifactLimits.SvgMaxBytes);
        VisualArtifactLimits.SvgMaxBytes.ShouldBe(1L * 1024 * 1024);
    }

    [Fact(DisplayName = "Given a png payload, when MaxBytesFor is called, then it returns the 5 MiB cap")]
    public void ReturnsPngCap()
    {
        VisualArtifactLimits.MaxBytesFor("image/png").ShouldBe(VisualArtifactLimits.PngMaxBytes);
        VisualArtifactLimits.PngMaxBytes.ShouldBe(5L * 1024 * 1024);
    }

    [Theory(DisplayName = "Given a mime outside the allow-list, when MaxBytesFor is called, then it returns null")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNullForUnknownMime(string? contentType)
    {
        VisualArtifactLimits.MaxBytesFor(contentType).ShouldBeNull();
    }

    [Theory(DisplayName = "Given a known mime, when the Is* predicates are called, then they classify correctly")]
    [InlineData("text/html", true, false, false)]
    [InlineData("image/svg+xml", false, true, true)]
    [InlineData("image/png", false, false, true)]
    [InlineData("application/octet-stream", false, false, false)]
    public void ClassifiesMimePredicates(string contentType, bool expectedHtml, bool expectedSvg, bool expectedImage)
    {
        VisualArtifactLimits.IsHtml(contentType).ShouldBe(expectedHtml);
        VisualArtifactLimits.IsSvg(contentType).ShouldBe(expectedSvg);
        VisualArtifactLimits.IsImage(contentType).ShouldBe(expectedImage);
    }

    [Fact(DisplayName = "Given a freshly-minted artifact id, when ToString is called, then it returns the underlying GUID text")]
    public void VisualArtifactIdRendersAsGuid()
    {
        var id = VisualArtifactId.New();
        id.ToString().ShouldBe(id.Value.ToString());
    }
}
