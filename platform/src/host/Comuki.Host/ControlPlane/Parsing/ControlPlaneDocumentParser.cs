using System.Text.RegularExpressions;

namespace Comuki.Host.ControlPlane.Parsing;

/// <summary>
/// Pure parser for control-plane markdown documents: a YAML-ish frontmatter
/// block (<c>---</c> fences) carrying <c>name</c> / <c>description</c> plus
/// optional <c>allowedTools</c> / <c>model</c>, followed by the body. Mirrors
/// the TS reader in <c>agents/comuki-agent-core/src/rules/reader.ts</c> -
/// the same scalar/list subset, the same tolerance - so the C# catalog and
/// the worker-side TS loaders read identical content. No I/O, no state.
/// </summary>
public static partial class ControlPlaneDocumentParser
{
    /// <summary>Frontmatter key: document name (required, non-empty).</summary>
    public const string NameKey = "name";

    /// <summary>Frontmatter key: short description (required).</summary>
    public const string DescriptionKey = "description";

    /// <summary>Frontmatter key: tool allow-list for worker profiles (optional; list, or scalar as a single item).</summary>
    public const string AllowedToolsKey = "allowedTools";

    /// <summary>Frontmatter key: model role hint for routing (optional, scalar).</summary>
    public const string ModelKey = "model";

    /// <summary>
    /// Parses one document. Returns null when the text has no frontmatter
    /// block, no closing fence, or lacks a non-empty name and description.
    /// Listing many documents must not throw on one malformed entry.
    /// </summary>
    /// <param name="text"></param>
    public static ControlPlaneDocument? Parse(string text)
    {
        var extracted = FrontmatterParsing.Extract(text);
        if (extracted is null)
        {
            return null;
        }

        var fields = FrontmatterParsing.ParseYamlish(extracted.Yaml);
        var name = FrontmatterParsing.Scalar(fields, NameKey);
        var description = FrontmatterParsing.Scalar(fields, DescriptionKey);
        return string.IsNullOrWhiteSpace(name) || description is null
            ? null
            : new ControlPlaneDocument(
            name,
            description,
            FrontmatterParsing.List(fields, AllowedToolsKey),
            FrontmatterParsing.Scalar(fields, ModelKey),
            extracted.Body);
    }

    /// <summary>Frontmatter key line: <c>key: value</c> with an ASCII key.</summary>
    /// <returns></returns>
    [GeneratedRegex(@"^([A-Za-z][\w.-]*)\s*:\s*(.*)$")]
    public static partial Regex KeyPattern();

    /// <summary>Block-list item line: whitespace, dash, then the item.</summary>
    /// <returns></returns>
    [GeneratedRegex(@"^\s+-\s+(.*)$")]
    public static partial Regex BlockItemPattern();

    /// <summary>Flow list value: <c>[a, b, c]</c>.</summary>
    /// <returns></returns>
    [GeneratedRegex(@"^\[(.*)\]$")]
    public static partial Regex FlowListPattern();
}

/// <summary>
/// YAML-subset parsing helpers, one-to-one with the TS reader: key/value
/// scalars, flow lists (<c>[a, b]</c>), block lists (<c>- item</c> under an
/// empty value), and <c>#</c> comments. Nested structures and tags are
/// ignored.
/// </summary>
file static class FrontmatterParsing
{
    public static ExtractedFrontmatter? Extract(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        var endIndex = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            if (lines[index].Trim() == "---")
            {
                endIndex = index;
                break;
            }
        }

        return endIndex < 0
            ? null
            : new ExtractedFrontmatter(
            string.Join('\n', lines[1..endIndex]),
            string.Join('\n', lines[(endIndex + 1)..]));
    }

    public static Dictionary<string, FrontmatterField> ParseYamlish(string yaml)
    {
        var result = new Dictionary<string, FrontmatterField>();
        var lines = yaml.Split('\n');
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

            var keyMatch = ControlPlaneDocumentParser.KeyPattern().Match(trimmed);
            if (!keyMatch.Success)
            {
                continue;
            }

            var key = keyMatch.Groups[1].Value;
            var value = keyMatch.Groups[2].Value.Trim();

            if (value.Length == 0)
            {
                var blockList = TakeBlockListItems(lines, index);
                if (blockList.Values.Count > 0)
                {
                    result[key] = new FrontmatterField(null, blockList.Values);
                    index = blockList.NextIndex;
                }

                continue;
            }

            var flowMatch = ControlPlaneDocumentParser.FlowListPattern().Match(value);
            result[key] = flowMatch.Success
                ? new FrontmatterField(null, SplitFlowList(flowMatch.Groups[1].Value))
                : new FrontmatterField(StripQuotes(value), []);
        }

        return result;
    }

    public static string? Scalar(Dictionary<string, FrontmatterField> fields, string key)
    {
        return fields.TryGetValue(key, out var field) ? field.Scalar : null;
    }

    public static IReadOnlyList<string> List(Dictionary<string, FrontmatterField> fields, string key)
    {
        if (!fields.TryGetValue(key, out var field))
        {
            return [];
        }

        // A scalar value degrades to a single-item list - plain YAML semantics.
        return field.Scalar is { } scalar ? [scalar] : field.Items;
    }

    public static BlockListScan TakeBlockListItems(string[] lines, int startIndex)
    {
        var values = new List<string>();
        var index = startIndex;

        while (index < lines.Length)
        {
            var itemMatch = ControlPlaneDocumentParser.BlockItemPattern().Match(lines[index]);
            if (!itemMatch.Success)
            {
                break;
            }

            values.Add(StripQuotes(itemMatch.Groups[1].Value.Trim()));
            index++;
        }

        return new BlockListScan(values, index);
    }

    public static IReadOnlyList<string> SplitFlowList(string content)
    {
        return [.. content
            .Split(',')
            .Select(static part => StripQuotes(part.Trim()))
            .Where(static part => part.Length > 0)];
    }

    public static string StripQuotes(string value)
    {
        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                return value[1..^1];
            }
        }

        return value;
    }
}

/// <summary>The split of a document: the raw YAML-ish frontmatter text and the body after the closing fence.</summary>
/// <param name="Yaml"></param>
/// <param name="Body"></param>
file sealed record ExtractedFrontmatter(string Yaml, string Body);

/// <summary>One frontmatter field: either a scalar or a list - exactly one of the two is set.</summary>
/// <param name="Scalar"></param>
/// <param name="Items"></param>
file sealed record FrontmatterField(string? Scalar, IReadOnlyList<string> Items);

/// <summary>Result of scanning consecutive block-list item lines: the values and where scanning stopped.</summary>
/// <param name="Values"></param>
/// <param name="NextIndex"></param>
file sealed record BlockListScan(IReadOnlyList<string> Values, int NextIndex);
