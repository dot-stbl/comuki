using Comuki.Host.ControlPlane.Parsing;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.ProfileCatalog;

/// <summary>
/// Unit tests for <see cref="SkillDocumentParser"/> — locks parser parity with
/// the TS reader + loader in
/// <c>agents/comuki-agent-core/src/rules/reader.ts</c> +
/// <c>agents/comuki-worker-sdk/src/skills/loader.ts</c>:
/// <list type="bullet">
///   <item>strict superset: documents without the new keys parse exactly as
///     before (same shape, no required new fields);</item>
///   <item>the parser accepts the spec example <c>validate_against</c> with
///     a mix of plain-string paths and SourceRef-shaped objects;</item>
///   <item>the version defaults to <c>0.1.0</c> when absent (task 25.4).</item>
/// </list>
/// </summary>
public sealed class SkillDocumentParserShould
{
    [Fact(DisplayName = "Given a bare skill document, when parsed, then version defaults to 0.1.0 and the new lists are empty")]
    public void BareDocumentDefaultsVersionToPointOnePointZero()
    {
        var text = """
                   ---
                   name: git-workflow
                   description: Safe branch and commit flow
                   ---

                   Branch first.
                   """;

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.Name.ShouldBe("git-workflow");
        document.Description.ShouldBe("Safe branch and commit flow");
        document.Version.ShouldBe("0.1.0");
        document.TriggerWhen.ShouldBeEmpty();
        document.ValidateAgainstPaths.ShouldBeEmpty();
        document.ValidateAgainstRefs.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given an explicit version, when parsed, then the version is taken verbatim")]
    public void ParseExplicitVersion()
    {
        var text = """
                   ---
                   name: citation-cleanup
                   description: Cleans up inline citations
                   version: 1.2.0
                   ---

                   Body.
                   """;

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.Version.ShouldBe("1.2.0");
    }

    [Fact(DisplayName = "Given a scalar trigger_when, when parsed, then it degrades to a single-item list")]
    public void ParseScalarTriggerWhenAsSingleItemList()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: t
            description: d
            trigger_when: drafting a long-form document that needs citation cleanup
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.TriggerWhen.ShouldBe([
            "drafting a long-form document that needs citation cleanup",
        ]);
    }

    [Fact(DisplayName = "Given a flow-list trigger_when, when parsed, then the items split on commas")]
    public void ParseFlowListTriggerWhen()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: t
            description: d
            trigger_when: [a, b, c]
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.TriggerWhen.ShouldBe(["a", "b", "c"]);
    }

    [Fact(DisplayName = "Given a block-list trigger_when, when parsed, then the items are taken one per line")]
    public void ParseBlockListTriggerWhen()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: t
            description: d
            trigger_when:
              - a
              - b
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.TriggerWhen.ShouldBe(["a", "b"]);
    }

    [Fact(DisplayName = "Given a scalar validate_against, when parsed, then it degrades to a single-item paths list")]
    public void ParseScalarValidateAgainst()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: v
            description: d
            validate_against: ../../rules/citation-format.md
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.ValidateAgainstPaths.ShouldBe(["../../rules/citation-format.md"]);
        document.ValidateAgainstRefs.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a block-list validate_against with mixed paths and objects, when parsed, then the split is correct")]
    public void ParseMixedValidateAgainst()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: v
            description: d
            validate_against:
              - "../../rules/citation-format.md"
              - { kind: knowledge, id: "doc/citation-style@v3" }
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.ValidateAgainstPaths.ShouldBe(["../../rules/citation-format.md"]);
        document.ValidateAgainstRefs.ShouldHaveSingleItem();
        document.ValidateAgainstRefs[0]["kind"].ShouldBe("knowledge");
        document.ValidateAgainstRefs[0]["id"].ShouldBe("doc/citation-style@v3");
    }

    [Fact(DisplayName = "Given a flow-list validate_against with mixed paths and objects, when parsed, then the split is correct")]
    public void ParseFlowListValidateAgainst()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: v
            description: d
            validate_against: ["a.md", { kind: control, id: "rule-x" }]
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.ValidateAgainstPaths.ShouldBe(["a.md"]);
        document.ValidateAgainstRefs.ShouldHaveSingleItem();
        document.ValidateAgainstRefs[0]["kind"].ShouldBe("control");
        document.ValidateAgainstRefs[0]["id"].ShouldBe("rule-x");
    }

    [Fact(DisplayName = "Given the spec example, when parsed, then the catalog gets full metadata and the body is preserved")]
    public void ParseSpecExample()
    {
        var text = """
                   ---
                   name: citation-cleanup
                   description: Cleans up inline citations
                   trigger_when: drafting a long-form document that needs citation cleanup
                   validate_against:
                     - "../../rules/citation-format.md"
                     - { kind: knowledge, id: "doc/citation-style@v3" }
                   version: 1.2.0
                   ---

                   Strip duplicates, normalise.
                   """;

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.Name.ShouldBe("citation-cleanup");
        document.Description.ShouldBe("Cleans up inline citations");
        document.Version.ShouldBe("1.2.0");
        document.TriggerWhen.ShouldBe([
            "drafting a long-form document that needs citation cleanup",
        ]);
        document.ValidateAgainstPaths.ShouldBe(["../../rules/citation-format.md"]);
        document.ValidateAgainstRefs.ShouldHaveSingleItem();
        document.ValidateAgainstRefs[0]["kind"].ShouldBe("knowledge");
        document.ValidateAgainstRefs[0]["id"].ShouldBe("doc/citation-style@v3");
        document.Body.ShouldContain("Strip duplicates");
    }

    [Fact(DisplayName = "Given CRLF line endings, when parsed, then the document parses the same as with LF")]
    public void TolerateCrlfLineEndings()
    {
        var text = "---\r\nname: crlf\r\ndescription: Windows-authored.\r\n---\r\n\r\nBody.";

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.Name.ShouldBe("crlf");
        document.Body.ShouldBe("\nBody.");
    }

    [Fact(DisplayName = "Given a flow-mapped object with a quoted value, when parsed, then the quotes are stripped")]
    public void StripQuotesInsideFlowMappedObject()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: q
            description: d
            validate_against: { kind: "control", id: 'rule-y' }
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.ValidateAgainstRefs.ShouldHaveSingleItem();
        document.ValidateAgainstRefs[0]["kind"].ShouldBe("control");
        document.ValidateAgainstRefs[0]["id"].ShouldBe("rule-y");
    }

    [Fact(DisplayName = "Given a flow-mapped object missing a colon, when parsed, then the entry is treated as a path")]
    public void MalformedFlowObjectFallsBackToPath()
    {
        var text = SkillDocuments.WithYaml(
            """
            name: bad
            description: d
            validate_against: not-a-valid-object
            """);

        var document = SkillDocumentParser.Parse(text);

        document.ShouldNotBeNull();
        document!.ValidateAgainstPaths.ShouldBe(["not-a-valid-object"]);
        document.ValidateAgainstRefs.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a frontmatter block without a closing fence, when parsed, then returns null")]
    public void ReturnNullWhenClosingFenceMissing()
    {
        var document = SkillDocumentParser.Parse("---\nname: stuck\ndescription: Never closes.\n");

        document.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a document without frontmatter, when parsed, then returns null")]
    public void ReturnNullWhenFrontmatterMissing()
    {
        var document = SkillDocumentParser.Parse("# Just markdown\n\nNo frontmatter here.");

        document.ShouldBeNull();
    }
}

/// <summary>Builds a document from frontmatter lines with a standard fence and body.</summary>
file static class SkillDocuments
{
    public static string WithYaml(string yaml)
    {
        return $"---\n{yaml}\n---\n\nYou are the body.";
    }
}
