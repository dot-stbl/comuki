using Comuki.Host.ControlPlane.Parsing;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.ProfileCatalog;

/// <summary>
/// Unit tests for <see cref="ControlPlaneDocumentParser"/>. Locks parity with
/// the TS reader (agents/comuki-agent-core/src/rules/reader.ts): the same
/// frontmatter subset, the same tolerance - a malformed document yields null,
/// never an exception.
/// </summary>
public sealed class ControlPlaneDocumentParserShould
{
    [Fact(DisplayName = "Given a profile document with a block-list, when parsed, then name, description, allowedTools, model and body are extracted")]
    public void ParseProfileDocumentWithBlockList()
    {
        var text = """
                   ---
                   name: explore-readonly
                   description: Read-only explorer.
                   allowedTools:
                     - Read
                     - Grep
                     - Glob
                   model: light
                   ---

                   You are the body.
                   """;

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.Name.ShouldBe("explore-readonly");
        document.Description.ShouldBe("Read-only explorer.");
        document.AllowedTools.ShouldBe(["Read", "Grep", "Glob"]);
        document.Model.ShouldBe("light");
        document.Body.ShouldStartWith("\nYou are the body.");
    }

    [Fact(DisplayName = "Given a flow-list allowedTools, when parsed, then the items split on commas")]
    public void ParseFlowList()
    {
        var text = """
                   ---
                   name: implement
                   description: Implements things.
                   allowedTools: [Read, Write, "Bash", 'Grep']
                   ---

                   Body.
                   """;

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.AllowedTools.ShouldBe(["Read", "Write", "Bash", "Grep"]);
    }

    [Fact(DisplayName = "Given a scalar allowedTools, when parsed, then it degrades to a single-item list")]
    public void DegradeScalarAllowedToolsToSingleItem()
    {
        var text = ProfileDocuments.WithYaml(
            """
            name: mini
            description: Minimal profile.
            allowedTools: Read
            """);

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.AllowedTools.ShouldBe(["Read"]);
    }

    [Fact(DisplayName = "Given a document without frontmatter, when parsed, then returns null")]
    public void ReturnNullWhenFrontmatterMissing()
    {
        var document = ControlPlaneDocumentParser.Parse("# Just markdown\n\nNo frontmatter here.");

        document.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a frontmatter block without a closing fence, when parsed, then returns null")]
    public void ReturnNullWhenClosingFenceMissing()
    {
        var document = ControlPlaneDocumentParser.Parse("---\nname: stuck\ndescription: Never closes.\n");

        document.ShouldBeNull();
    }

    [Theory(DisplayName = "Given a blank or missing name, when parsed, then returns null")]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnNullWhenNameIsBlank(string name)
    {
        var text = ProfileDocuments.WithYaml($"""
                                              name: {name}
                                              description: Has description.
                                              """);

        var document = ControlPlaneDocumentParser.Parse(text);

        document.ShouldBeNull();
    }

    [Fact(DisplayName = "Given a missing description, when parsed, then returns null")]
    public void ReturnNullWhenDescriptionMissing()
    {
        var text = ProfileDocuments.WithYaml("name: no-description");

        var document = ControlPlaneDocumentParser.Parse(text);

        document.ShouldBeNull();
    }

    [Fact(DisplayName = "Given comments and quoted values in frontmatter, when parsed, then comments are skipped and quotes stripped")]
    public void IgnoreCommentsAndStripQuotes()
    {
        var text = """
                   ---
                   # worker profile
                   name: "quoted-name"
                   description: 'single quoted description'
                   ---

                   Body.
                   """;

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.Name.ShouldBe("quoted-name");
        document.Description.ShouldBe("single quoted description");
    }

    [Fact(DisplayName = "Given CRLF line endings, when parsed, then the document parses the same as with LF")]
    public void TolerateCrlfLineEndings()
    {
        var text = "---\r\nname: crlf\r\ndescription: Windows-authored.\r\n---\r\n\r\nBody.";

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.Name.ShouldBe("crlf");
        document.Description.ShouldBe("Windows-authored.");
        document.Body.ShouldBe("\nBody.");
    }

    [Fact(DisplayName = "Given an empty allowedTools key with no items, when parsed, then allowedTools is empty")]
    public void ReturnEmptyAllowedToolsForKeyWithoutItems()
    {
        var text = ProfileDocuments.WithYaml(
            """
            name: empty-tools
            description: No tools listed.
            allowedTools:
            model:
            """);

        var document = ControlPlaneDocumentParser.Parse(text);

        _ = document.ShouldNotBeNull();
        document.AllowedTools.ShouldBeEmpty();
        document.Model.ShouldBeNull();
    }
}

/// <summary>Builds a document from frontmatter lines with a standard fence and body.</summary>
file static class ProfileDocuments
{
    public static string WithYaml(string yaml)
    {
        return $"---\n{yaml}\n---\n\nYou are the body.";
    }
}
