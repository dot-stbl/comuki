using Comuki.Modules.Knowledge.Domain;
using Comuki.Shared.Kernel;

namespace Comuki.Modules.Knowledge.Infrastructure.Persistence;

/// <summary>
/// Wiki-specific frontmatter subset: <c>kind</c> (scalar) and
/// <c>link_graph</c> (flow list of <c>{ target, kind }</c> flow-mapped
/// objects, or a block list with the same item shape). The subset
/// intentionally stays small — wiki pages re-use the same chunker +
/// embedder as the rest of Knowledge, and the frontmatter is metadata
/// for the page id / link graph, not a full document model.
///
/// The generic fence / pattern / strip / split primitives live in
/// <see cref="YamlishFrontmatter"/>; this class layers the Wiki shape
/// (link entries, kind key, malformed-entry tolerance) on top.
/// </summary>
/// <remarks>
/// Parsing is permissive: a malformed entry drops the offending item,
/// never fails the whole page. A page with no frontmatter is a normal
/// chunkable body — the metadata stays null.
/// </remarks>
public static class WikiFrontmatter
{
    /// <summary>Frontmatter key: Wiki page kind (glossary | how-to | decision-record | incident | reference).</summary>
    public const string KindKey = "kind";

    /// <summary>Frontmatter key: link-graph adjacency list (flow list of flow-mapped objects).</summary>
    public const string LinkGraphKey = "link_graph";

    /// <summary>
    /// Extracts the Wiki frontmatter from a markdown body. Returns
    /// <c>null</c> when the body has no frontmatter block; returns an
    /// empty <see cref="WikiIngestMetadata"/> when the block carries no
    /// recognised keys.
    /// </summary>
    /// <param name="body">Markdown body, with or without a leading frontmatter block.</param>
    public static WikiIngestMetadata? ExtractMetadata(string body)
    {
        var extracted = YamlishFrontmatter.Extract(body);
        if (extracted is not { } frontmatter)
        {
            return null;
        }

        var metadata = WikiFrontmatterFields.ParseFields(frontmatter.Yaml);
        return metadata.Kind is null && metadata.LinkGraph.Count == 0
            ? new WikiIngestMetadata(null, [])
            : metadata;
    }

    /// <summary>Returns the body after the frontmatter block (the actual markdown content), or the original body when none.</summary>
    /// <param name="body"></param>
    public static string StripFrontmatter(string body)
    {
        var extracted = YamlishFrontmatter.Extract(body);
        return extracted is not { } frontmatter ? body : frontmatter.Body;
    }
}

/// <summary>
/// Key-scan + value classification for Wiki frontmatter: <c>kind</c>
/// (scalar) and <c>link_graph</c> (flow list or block list of
/// <c>{ target, kind }</c> flow-mapped objects). Lives next to
/// <see cref="WikiFrontmatter"/>; the shared fence / pattern / strip /
/// split helpers live in <see cref="YamlishFrontmatter"/>.
/// </summary>
file static class WikiFrontmatterFields
{
    /// <summary>
    /// Walks the YAML lines once, picking up <c>kind</c> (scalar) and
    /// <c>link_graph</c> (flow list or block list). Unknown keys are
    /// ignored — a malformed page keeps the keys it does recognise and
    /// the body still parses.
    /// </summary>
    /// <param name="yaml">The YAML body between the frontmatter fences.</param>
    public static WikiIngestMetadata ParseFields(string yaml)
    {
        var lines = yaml.Split('\n');
        WikiPageKind? kind = null;
        IReadOnlyList<WikiPageLink> links = [];
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            index++;

            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = YamlishFrontmatter.KeyPattern().Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();

            if (key == WikiFrontmatter.KindKey)
            {
                kind = WikiPageKindKeys.Parse(YamlishFrontmatter.StripQuotes(value));
                continue;
            }

            if (key == WikiFrontmatter.LinkGraphKey)
            {
                links = value.Length > 0
                    ? ParseFlowList(value)
                    : TakeBlockListLinks(lines, ref index);
            }
        }

        return new WikiIngestMetadata(kind, links);
    }

    /// <summary>
    /// Parses a single-line flow list of <c>{ target, kind }</c>
    /// objects — <c>[{ target: "…", kind: see-also }, …]</c>. Each
    /// entry is delegated to <see cref="TryParseLinkEntry"/>; entries
    /// that fail to parse (missing fields, unknown kinds) are dropped,
    /// not fatal.
    /// </summary>
    /// <param name="value">The right-hand side of the <c>link_graph</c> line, including the surrounding <c>[…]</c>.</param>
    public static IReadOnlyList<WikiPageLink> ParseFlowList(string value)
    {
        var flowMatch = YamlishFrontmatter.FlowListPattern().Match(value);
        if (!flowMatch.Success)
        {
            return [];
        }

        var items = YamlishFrontmatter.SplitTopLevelCommas(flowMatch.Groups[1].Value);
        var links = new List<WikiPageLink>(items.Count);
        foreach (var item in items)
        {
            if (TryParseLinkEntry(item) is { } link)
            {
                links.Add(link);
            }
        }

        return links;
    }

    /// <summary>
    /// Block-list form: each item lives on a line that starts with
    /// whitespace + dash, and the body is a single-line
    /// <c>{ target, kind }</c> flow-mapped object. The dash-prefixed
    /// continuation lines (the multi-line object form) are not part of
    /// the documented Wiki subset; the spec only shows the flow-list
    /// shape and the single-line block form is enough for callers.
    /// </summary>
    /// <param name="lines">All YAML lines.</param>
    /// <param name="index">Index immediately after the <c>link_graph:</c> line; advanced past the items.</param>
    public static IReadOnlyList<WikiPageLink> TakeBlockListLinks(string[] lines, ref int index)
    {
        var links = new List<WikiPageLink>();

        while (index < lines.Length)
        {
            var line = lines[index];
            var match = YamlishFrontmatter.BlockItemPattern().Match(line);
            if (!match.Success)
            {
                break;
            }

            var body = match.Groups[1].Value.Trim();
            if (TryParseLinkEntry(body) is { } link)
            {
                links.Add(link);
            }

            index++;
        }

        return links;
    }

    /// <summary>
    /// Single-line flow-mapped object probe: returns a
    /// <see cref="WikiPageLink"/> when the body is wrapped in matched
    /// <c>{</c>/<c>}</c> braces and carries a parseable <c>target</c>
    /// guid + <c>kind</c> string; otherwise null. Missing fields,
    /// unparseable kinds, and non-brace bodies all fall through to
    /// null so the caller skips the entry.
    /// </summary>
    /// <param name="raw">The trimmed body of a flow-list item or block-list item.</param>
    public static WikiPageLink? TryParseLinkEntry(string raw)
    {
        if (raw is not { Length: > 1 } body || body[0] != '{' || body[^1] != '}')
        {
            return null;
        }

        var entries = YamlishFrontmatter.SplitTopLevelCommas(body[1..^1]);
        string? target = null;
        string? kind = null;
        foreach (var entry in entries)
        {
            var colonIndex = entry.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var key = entry[..colonIndex].Trim();
            var value = entry[(colonIndex + 1)..].Trim();
            if (key == "target")
            {
                target = YamlishFrontmatter.StripQuotes(value);
            }
            else if (key == "kind")
            {
                kind = YamlishFrontmatter.StripQuotes(value);
            }
        }

        return !Guid.TryParse(target, out var targetGuid) || kind is null
            ? null
            : WikiPageLinkKindKeys.Parse(kind) is not { } parsedKind
                ? null
                : new WikiPageLink(new WikiPageId(targetGuid), parsedKind);
    }
}
