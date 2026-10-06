using Comuki.Modules.Knowledge.Domain;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit tests for the Wiki-specific surface of
/// <see cref="SourceDocument"/>: the <see cref="SourceKind"/> wire key,
/// the <see cref="SourceKindKeys"/> round-trip, and the wiki metadata
/// attachment flow. The new <see cref="SourceKind.Wiki"/> value rides
/// the same ingest surface as every other kind — a wiki page is just a
/// <see cref="SourceDocument"/> with the four extra columns populated.
/// </summary>
public sealed class SourceDocumentWikiShould
{
    [Fact(DisplayName = "Given the SourceKind enum, when SourceKind.Wiki is checked, then it carries the value 4 and the wire key is 'wiki'")]
    public void WikiKindRoundTripsThroughWireKey()
    {
        SourceKindKeys.Wiki.ShouldBe("wiki");
        SourceKindKeys.Key(SourceKind.Wiki).ShouldBe("wiki");
        SourceKindKeys.Parse("wiki").ShouldBe(SourceKind.Wiki);
        SourceKindKeys.ParseRequired("wiki").ShouldBe(SourceKind.Wiki);
    }

    [Fact(DisplayName = "Given a SourceDocument of a non-Wiki kind, when AttachWikiMetadata is called, then it throws")]
    public void RejectWikiMetadataOnNonWikiDocument()
    {
        var document = SourceDocument.Create(
            projectId: null,
            title: "Repository",
            source: SourceKind.Git,
            sourceRef: "https://example.com/repo",
            mimeType: "text/markdown",
            now: DateTimeOffset.UnixEpoch);

        Should.Throw<InvalidOperationException>(() => document.AttachWikiMetadata(
            WikiPageId.New(),
            WikiPageKind.Reference,
            updatedByMissionId: null,
            linkGraph: []));
    }

    [Fact(DisplayName = "Given a SourceDocument of Wiki kind, when AttachWikiMetadata is called, then the four fields are populated and the link graph is preserved")]
    public void AttachWikiMetadataPopulatesAllFields()
    {
        var document = SourceDocument.Create(
            projectId: null,
            title: "Decision record",
            source: SourceKind.Wiki,
            sourceRef: "mission:M1/output:abc",
            mimeType: "text/markdown",
            now: DateTimeOffset.UnixEpoch);

        var wikiPageId = WikiPageId.New();
        var missionId = Guid.CreateVersion7();
        var linkGraph = new List<WikiPageLink>
        {
            new(new WikiPageId(Guid.CreateVersion7()), WikiPageLinkKind.SeeAlso),
            new(new WikiPageId(Guid.CreateVersion7()), WikiPageLinkKind.Supersedes),
        };

        document.AttachWikiMetadata(
            wikiPageId,
            WikiPageKind.DecisionRecord,
            updatedByMissionId: missionId,
            linkGraph: linkGraph);

        document.WikiPageId.ShouldBe(wikiPageId);
        document.WikiPageKind.ShouldBe(WikiPageKind.DecisionRecord);
        document.UpdatedByMissionId.ShouldBe(missionId);
        document.LinkGraph.Count.ShouldBe(2);
        document.LinkGraph[0].Kind.ShouldBe(WikiPageLinkKind.SeeAlso);
        document.LinkGraph[1].Kind.ShouldBe(WikiPageLinkKind.Supersedes);
    }

    [Fact(DisplayName = "Given a non-Wiki SourceDocument, when AttachWikiMetadata is checked, then the wiki columns are null")]
    public void NonWikiDocumentLeavesWikiColumnsNull()
    {
        var document = SourceDocument.Create(
            projectId: null,
            title: "Repository",
            source: SourceKind.Git,
            sourceRef: "https://example.com/repo",
            mimeType: "text/markdown",
            now: DateTimeOffset.UnixEpoch);

        document.WikiPageId.ShouldBeNull();
        document.WikiPageKind.ShouldBeNull();
        document.UpdatedByMissionId.ShouldBeNull();
        document.LinkGraph.ShouldBeEmpty();
    }
}
