using Comuki.Modules.Knowledge.Domain;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit tests for <see cref="WikiFrontmatter"/> — the inline frontmatter
/// subset the Wiki ingest path reads from the body of a <c>source=wiki</c>
/// <c>knowledge.ingest</c> call. Mirrors the
/// <c>openspec/changes/add-mission-cowork/specs/knowledge/spec.md</c> shape
/// (<c>kind</c>, <c>link_graph</c>, <c>supersedes</c>). Permissive on
/// malformed entries: a broken link is dropped, not fatal.
/// </summary>
public sealed class WikiFrontmatterShould
{
    [Fact(DisplayName = "Given a body with no frontmatter, when parsed, then metadata is null and StripFrontmatter returns the body")]
    public void NullMetadataWhenNoFrontmatter()
    {
        const string body = "# Just markdown\n\nNo frontmatter here.";

        WikiFrontmatter.ExtractMetadata(body).ShouldBeNull();
        WikiFrontmatter.StripFrontmatter(body).ShouldBe(body);
    }

    [Fact(DisplayName = "Given a body with a kind-only frontmatter, when parsed, then the kind is captured and the link graph is empty")]
    public void ParseKindOnly()
    {
        const string body = """
                            ---
                            kind: decision-record
                            ---

                            Body content.
                            """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.Kind.ShouldBe(WikiPageKind.DecisionRecord);
        metadata.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with an unknown kind, when parsed, then the kind is null and the body still parses")]
    public void ParseUnknownKindYieldsNull()
    {
        const string body = """
                            ---
                            kind: never-heard-of
                            ---

                            Body.
                            """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.Kind.ShouldBeNull();
        metadata.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with a flow-list link_graph, when parsed, then each entry becomes a WikiPageLink")]
    public void ParseFlowListLinkGraph()
    {
        var targetA = Guid.CreateVersion7();
        var targetB = Guid.CreateVersion7();
        var body = $$"""
                    ---
                    kind: glossary
                    link_graph:
                      - { target: "{{targetA}}", kind: see-also }
                      - { target: "{{targetB}}", kind: supersedes }
                    ---

                    Body.
                    """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.Count.ShouldBe(2);
        metadata.LinkGraph[0].TargetPageId.Value.ShouldBe(targetA);
        metadata.LinkGraph[0].Kind.ShouldBe(WikiPageLinkKind.SeeAlso);
        metadata.LinkGraph[1].TargetPageId.Value.ShouldBe(targetB);
        metadata.LinkGraph[1].Kind.ShouldBe(WikiPageLinkKind.Supersedes);
    }

    [Fact(DisplayName = "Given a body with a flow-list link_graph that mixes kind values, when parsed, then the kind round-trips per entry")]
    public void ParseFlowListLinkGraphMixedKinds()
    {
        var targetA = Guid.CreateVersion7();
        var body = $$"""
                    ---
                    kind: reference
                    link_graph:
                      - { target: "{{targetA}}", kind: derived-from }
                    ---

                    Body.
                    """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.ShouldHaveSingleItem();
        metadata.LinkGraph[0].TargetPageId.Value.ShouldBe(targetA);
        metadata.LinkGraph[0].Kind.ShouldBe(WikiPageLinkKind.DerivedFrom);
    }

    [Fact(DisplayName = "Given a body with a block-list link_graph, when parsed, then each item becomes a WikiPageLink")]
    public void ParseBlockListLinkGraph()
    {
        var targetA = Guid.CreateVersion7();
        var targetB = Guid.CreateVersion7();
        var body = $$"""
                    ---
                    kind: reference
                    link_graph:
                      - { target: "{{targetA}}", kind: derived-from }
                      - { target: "{{targetB}}", kind: supersedes }
                    ---

                    Body.
                    """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.Count.ShouldBe(2);
        metadata.LinkGraph[0].TargetPageId.Value.ShouldBe(targetA);
        metadata.LinkGraph[0].Kind.ShouldBe(WikiPageLinkKind.DerivedFrom);
        metadata.LinkGraph[1].TargetPageId.Value.ShouldBe(targetB);
        metadata.LinkGraph[1].Kind.ShouldBe(WikiPageLinkKind.Supersedes);
    }

    [Fact(DisplayName = "Given a body with a malformed link entry, when parsed, then the entry is dropped and the rest survives")]
    public void DropMalformedLinkEntry()
    {
        var goodTarget = Guid.CreateVersion7();
        var body = $$"""
                    ---
                    link_graph:
                      - not-an-object
                      - { target: "{{goodTarget}}", kind: see-also }
                    ---

                    Body.
                    """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.ShouldHaveSingleItem();
        metadata.LinkGraph[0].TargetPageId.Value.ShouldBe(goodTarget);
    }

    [Fact(DisplayName = "Given a body with a link entry missing the target, when parsed, then the entry is dropped")]
    public void DropLinkEntryMissingTarget()
    {
        const string body = """
                           ---
                           link_graph:
                             - { kind: see-also }
                           ---

                           Body.
                           """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with a link entry whose target is not a Guid, when parsed, then the entry is dropped")]
    public void DropLinkEntryWithNonGuidTarget()
    {
        const string body = """
                           ---
                           link_graph:
                             - { target: "not-a-uuid", kind: see-also }
                           ---

                           Body.
                           """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with an unknown link kind, when parsed, then the entry is dropped")]
    public void DropLinkEntryWithUnknownKind()
    {
        var target = Guid.CreateVersion7();
        var body = $$"""
                    ---
                    link_graph:
                      - { target: "{{target}}", kind: dunno }
                    ---

                    Body.
                    """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with no recognised keys, when parsed, then metadata carries no kind and an empty link graph")]
    public void EmptyMetadataWhenNoRecognisedKeys()
    {
        const string body = """
                           ---
                           title: ignored
                           version: 1.0.0
                           ---

                           Body.
                           """;

        var metadata = WikiFrontmatter.ExtractMetadata(body);

        metadata.ShouldNotBeNull();
        metadata!.Kind.ShouldBeNull();
        metadata.LinkGraph.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a body with a kind and link graph, when parsed, then StripFrontmatter returns the body content only")]
    public void StripFrontmatterRemovesYamlBlock()
    {
        const string body = """
                           ---
                           kind: how-to
                           link_graph:
                             - { target: "00000000-0000-0000-0000-000000000000", kind: see-also }
                           ---

                           # Heading

                           Body content.
                           """;

        var stripped = WikiFrontmatter.StripFrontmatter(body);

        stripped.ShouldNotContain("---");
        stripped.ShouldContain("# Heading");
        stripped.ShouldContain("Body content.");
    }
}
