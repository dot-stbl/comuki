using System.Text.RegularExpressions;
using Comuki.Modules.Procedures.Domain.Catalog;

namespace Comuki.Modules.Procedures.Domain.Loading;

/// <summary>
/// Pure parser for procedure-node-kind markdown documents. Reads a
/// frontmatter block (<c>---</c> fences) plus a body in the same shape as
/// the control-plane profile reader, but with the metadata the catalog
/// needs (owner surface, ports, evidence, idempotency, approval floor).
/// Returns null when the document is missing the frontmatter, missing
/// the closing fence, or lacking <c>key</c>/<c>title</c>/<c>description</c>
/// — the catalog reader treats a null document as a malformed entry and
/// must surface a loud refusal rather than silently dropping it.
/// </summary>
public static partial class NodeKindDescriptorDocumentParser
{
    /// <summary>Frontmatter key: file-stem identifier, must match the descriptor key.</summary>
    public const string KeyKey = "key";

    /// <summary>Frontmatter key: short display title.</summary>
    public const string TitleKey = "title";

    /// <summary>Frontmatter key: one-line description for catalogs.</summary>
    public const string DescriptionKey = "description";

    /// <summary>Frontmatter key: owning execution surface.</summary>
    public const string OwnerSurfaceKey = "owner";

    /// <summary>Frontmatter key: reference to the parameter schema (v1: a string name).</summary>
    public const string ParameterSchemaKey = "parameters";

    /// <summary>Frontmatter key: typed outcome ports (one per line under <c>ports</c>, or a flow list).</summary>
    public const string OutcomePortsKey = "ports";

    /// <summary>Frontmatter key: evidence types the instance must produce.</summary>
    public const string EvidenceRequirementsKey = "evidence";

    /// <summary>Frontmatter key: risk class (policy metadata).</summary>
    public const string RiskClassKey = "risk";

    /// <summary>Frontmatter key: idempotency declaration (<c>optional</c> / <c>required</c> / <c>inherent</c>).</summary>
    public const string IdempotencyKey = "idempotency";

    /// <summary>Frontmatter key: minimum distinct approvers (0, 1, or 2).</summary>
    public const string ApprovalFloorKey = "approval_floor";

    /// <summary>Frontmatter key: optional editions feature key for paid-only kinds.</summary>
    public const string EditionsFeatureKey = "editions";

    /// <summary>
    /// Parses one document. Returns null when the text has no frontmatter
    /// block, no closing fence, or lacks a non-empty key/title/description
    /// — those are the catalog-loader's hard requirement.
    /// </summary>
    /// <param name="text">Raw markdown text.</param>
    public static NodeKindDescriptorDocument? Parse(string text)
    {
        var extracted = FrontmatterExtract.Extract(text);
        if (extracted is null)
        {
            return null;
        }

        var fields = FrontmatterExtract.ParseFields(extracted.Value.Yaml);
        var key = FrontmatterExtract.Scalar(fields, KeyKey);
        var title = FrontmatterExtract.Scalar(fields, TitleKey);
        var description = FrontmatterExtract.Scalar(fields, DescriptionKey);

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(title) || description is null)
        {
            return null;
        }

        var idempotencyText = FrontmatterExtract.Scalar(fields, IdempotencyKey);
        var approvalText = FrontmatterExtract.Scalar(fields, ApprovalFloorKey);

        return new NodeKindDescriptorDocument(
            Key: key.Trim(),
            Title: title.Trim(),
            Description: description,
            OwnerSurface: FrontmatterExtract.Scalar(fields, OwnerSurfaceKey) ?? string.Empty,
            ParameterSchema: FrontmatterExtract.Scalar(fields, ParameterSchemaKey) ?? string.Empty,
            OutcomePorts: FrontmatterExtract.List(fields, OutcomePortsKey),
            EvidenceRequirements: FrontmatterExtract.List(fields, EvidenceRequirementsKey),
            RiskClass: FrontmatterExtract.Scalar(fields, RiskClassKey) ?? "low",
            Idempotency: string.IsNullOrWhiteSpace(idempotencyText) ? "inherent" : idempotencyText,
            ApprovalFloor: int.TryParse(approvalText, out var floor) ? floor : 0,
            EditionsFeatureKey: FrontmatterExtract.Scalar(fields, EditionsFeatureKey),
            Body: extracted.Value.Body);
    }

    /// <summary>Frontmatter key line: <c>key: value</c> with an ASCII key.</summary>
    [GeneratedRegex(@"^([A-Za-z][\w.-]*)\s*:\s*(.*)$")]
    public static partial Regex KeyPattern();

    /// <summary>Block-list item line: whitespace, dash, then the item.</summary>
    [GeneratedRegex(@"^\s+-\s+(.*)$")]
    public static partial Regex BlockItemPattern();

    /// <summary>Flow list value: <c>[a, b, c]</c>.</summary>
    [GeneratedRegex(@"^\[(.*)\]$")]
    public static partial Regex FlowListPattern();
}

/// <summary>
/// Frontmatter subset: <c>key: value</c> scalars and two list shapes
/// (block under an empty value, <c>[a, b]</c> flow). Mirrors
/// <c>Comuki.Host.ControlPlane.Parsing.FrontmatterParsing</c> — same
/// tolerance, same call shape — so profile and node-kind readers stay in
/// step on which inputs they accept. Pure: no I/O, no state.
/// </summary>
file static class FrontmatterExtract
{
    public static (string Yaml, string Body)? Extract(string text)
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
            : (
            string.Join('\n', lines[1..endIndex]),
            string.Join('\n', lines[(endIndex + 1)..]));
    }

    public static Dictionary<string, FrontmatterField> ParseFields(string yaml)
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

            var keyMatch = NodeKindDescriptorDocumentParser.KeyPattern().Match(trimmed);
            if (!keyMatch.Success)
            {
                continue;
            }

            var key = keyMatch.Groups[1].Value;
            var value = keyMatch.Groups[2].Value.Trim();

            if (value.Length == 0)
            {
                var (Values, NextIndex) = TakeBlockListItems(lines, index);
                if (Values.Count > 0)
                {
                    result[key] = new FrontmatterField(null, Values);
                    index = NextIndex;
                }

                continue;
            }

            var flowMatch = NodeKindDescriptorDocumentParser.FlowListPattern().Match(value);
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

        // A scalar value degrades to a single-item list — plain YAML semantics.
        return field.Scalar is { } scalar ? [scalar] : field.Items;
    }

    public static (List<string> Values, int NextIndex) TakeBlockListItems(string[] lines, int startIndex)
    {
        var values = new List<string>();
        var index = startIndex;

        while (index < lines.Length)
        {
            var itemMatch = NodeKindDescriptorDocumentParser.BlockItemPattern().Match(lines[index]);
            if (!itemMatch.Success)
            {
                break;
            }

            values.Add(StripQuotes(itemMatch.Groups[1].Value.Trim()));
            index++;
        }

        return (values, index);
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

/// <summary>One frontmatter field: either a scalar or a list — exactly one of the two is set.</summary>
/// <param name="Scalar"></param>
/// <param name="Items"></param>
file sealed record FrontmatterField(string? Scalar, IReadOnlyList<string> Items);
