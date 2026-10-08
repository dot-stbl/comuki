using Comuki.Shared.Kernel;

namespace Comuki.Host.ControlPlane.Parsing;

/// <summary>
/// Pure parser for control-plane markdown documents: a YAML-ish frontmatter
/// block (<c>---</c> fences) carrying <c>name</c> / <c>description</c> plus
/// optional <c>allowedTools</c> / <c>model</c>, followed by the body. Mirrors
/// the TS reader in <c>agents/comuki-agent-core/src/rules/reader.ts</c> -
/// the same scalar/list subset, the same tolerance - so the C# catalog and
/// the worker-side TS loaders read identical content. No I/O, no state.
///
/// The generic fence / pattern / strip / split primitives live in
/// <see cref="YamlishFrontmatter"/>; this class layers the control-plane
/// shape (scalar or list-of-strings, with the block-list shortcut under
/// an empty value) on top.
/// </summary>
public static class ControlPlaneDocumentParser
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
    public static ControlPlaneDocument? Parse(string text)
    {
        var extracted = YamlishFrontmatter.Extract(text);
        if (extracted is null)
        {
            return null;
        }

        var fields = FrontmatterFields.Parse(extracted.Yaml);
        var name = FrontmatterFields.Scalar(fields, NameKey);
        var description = FrontmatterFields.Scalar(fields, DescriptionKey);
        return string.IsNullOrWhiteSpace(name) || description is null
            ? null
            : new ControlPlaneDocument(
                name,
                description,
                FrontmatterFields.List(fields, AllowedToolsKey),
                FrontmatterFields.Scalar(fields, ModelKey),
                extracted.Body);
    }
}

/// <summary>
/// Key-scan + value classification for control-plane documents: every
/// field is either a scalar (single string) or a list of strings — no
/// flow-mapped objects here. Lives next to the parser that uses it; the
/// shared fence / strip / split helpers live in
/// <see cref="YamlishFrontmatter"/>.
/// </summary>
file static class FrontmatterFields
{
    public static Dictionary<string, ControlPlaneFrontmatterField> Parse(string yaml)
    {
        var result = new Dictionary<string, ControlPlaneFrontmatterField>();
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

            var keyMatch = YamlishFrontmatter.KeyPattern().Match(trimmed);
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
                    result[key] = new ControlPlaneFrontmatterField(null, blockList.Values);
                    index = blockList.NextIndex;
                }

                continue;
            }

            var flowMatch = YamlishFrontmatter.FlowListPattern().Match(value);
            result[key] = flowMatch.Success
                ? new ControlPlaneFrontmatterField(null, SplitFlowList(flowMatch.Groups[1].Value))
                : new ControlPlaneFrontmatterField(YamlishFrontmatter.StripQuotes(value), []);
        }

        return result;
    }

    public static string? Scalar(Dictionary<string, ControlPlaneFrontmatterField> fields, string key)
    {
        return fields.TryGetValue(key, out var field) ? field.Scalar : null;
    }

    public static IReadOnlyList<string> List(Dictionary<string, ControlPlaneFrontmatterField> fields, string key)
    {
        if (!fields.TryGetValue(key, out var field))
        {
            return [];
        }

        // A scalar value degrades to a single-item list - plain YAML semantics.
        return field.Scalar is { } scalar ? [scalar] : field.Items;
    }

    public static ControlPlaneBlockListScan TakeBlockListItems(string[] lines, int startIndex)
    {
        var values = new List<string>();
        var index = startIndex;

        while (index < lines.Length)
        {
            var itemMatch = YamlishFrontmatter.BlockItemPattern().Match(lines[index]);
            if (!itemMatch.Success)
            {
                break;
            }

            values.Add(YamlishFrontmatter.StripQuotes(itemMatch.Groups[1].Value.Trim()));
            index++;
        }

        return new ControlPlaneBlockListScan(values, index);
    }

    public static IReadOnlyList<string> SplitFlowList(string content)
    {
        // SplitTopLevelCommas tracks quotes / braces / brackets, so a
        // comma inside a quoted scalar or a nested flow list/object does
        // not split the surrounding list — Split(',') did, and a value
        // like [a, "b, c", d] was getting chopped. The shared helper
        // also trims each piece, so StripQuotes on the result is the
        // last step.
        return [.. YamlishFrontmatter.SplitTopLevelCommas(content)
            .Select(static piece => YamlishFrontmatter.StripQuotes(piece))
            .Where(static piece => piece.Length > 0)];
    }
}

/// <summary>One frontmatter field: either a scalar or a list — exactly one of the two is set.</summary>
/// <param name="Scalar">Set when the field is a scalar value.</param>
/// <param name="Items">Set when the field is a list of scalar values.</param>
file sealed record ControlPlaneFrontmatterField(string? Scalar, IReadOnlyList<string> Items);

/// <summary>Result of scanning consecutive block-list item lines: the values and where scanning stopped.</summary>
/// <param name="Values">The block-list items parsed in order.</param>
/// <param name="NextIndex">Index in the lines array immediately past the last block-list item.</param>
file sealed record ControlPlaneBlockListScan(IReadOnlyList<string> Values, int NextIndex);
