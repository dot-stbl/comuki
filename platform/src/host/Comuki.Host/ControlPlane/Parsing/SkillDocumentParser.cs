using Comuki.Shared.Kernel;

namespace Comuki.Host.ControlPlane.Parsing;

/// <summary>
/// Pure parser for control-plane skill documents: a YAML-ish frontmatter
/// block (<c>---</c> fences) carrying <c>name</c> / <c>description</c> plus
/// the task 25.x managed-asset metadata (<c>trigger_when</c> /
/// <c>validate_against</c> / <c>version</c>), followed by the body.
///
/// Mirrors the TS reader in
/// <c>agents/comuki-agent-core/src/rules/reader.ts</c> + the loader in
/// <c>agents/comuki-worker-sdk/src/skills/loader.ts</c> — the same scalar /
/// flow-list / flow-mapped-object subset, the same tolerance, so the C#
/// catalog and the worker-side TS loaders read identical content. The
/// extended shape (<c>{ kind: knowledge, id: "doc@v3" }</c> inside a flow
/// list, plus the three new keys) is a strict superset of the old profile /
/// chat-command parser. No I/O, no state.
///
/// The generic fence / pattern / strip / split primitives live in
/// <see cref="YamlishFrontmatter"/>; this class layers the skill shape
/// (each list item may be a scalar or a flow-mapped object — the brain
/// needs to iterate the two shapes separately) on top.
/// </summary>
public static class SkillDocumentParser
{
    /// <summary>Frontmatter key: document name (required, non-empty).</summary>
    public const string NameKey = "name";

    /// <summary>Frontmatter key: short description (required).</summary>
    public const string DescriptionKey = "description";

    /// <summary>Frontmatter key: natural-language hint for when the skill applies (task 25.1). Scalar or list.</summary>
    public const string TriggerWhenKey = "trigger_when";

    /// <summary>Frontmatter key: control-plane documents the skill depends on (task 25.1). Scalar, list, or mixed flow-mapped objects.</summary>
    public const string ValidateAgainstKey = "validate_against";

    /// <summary>Frontmatter key: semver of the skill (task 25.1). Defaults to <c>0.1.0</c> when absent (task 25.4).</summary>
    public const string VersionKey = "version";

    /// <summary>Default version returned when the frontmatter omits <c>version</c> — task 25.4.</summary>
    public const string DefaultVersion = "0.1.0";

    /// <summary>
    /// Parses one skill document. Returns null when the text has no frontmatter
    /// block, no closing fence, or lacks a non-empty name and description.
    /// Listing many documents must not throw on one malformed entry.
    /// </summary>
    public static SkillDocument? Parse(string text)
    {
        var extracted = YamlishFrontmatter.Extract(text);
        if (extracted is null)
        {
            return null;
        }

        var fields = SkillFields.Parse(extracted.Yaml);
        var name = SkillFields.Scalar(fields, NameKey);
        var description = SkillFields.Scalar(fields, DescriptionKey);
        if (string.IsNullOrWhiteSpace(name) || description is null)
        {
            return null;
        }

        var version = SkillFields.Scalar(fields, VersionKey);
        var triggerWhen = SkillFields.ScalarList(fields, TriggerWhenKey);
        var split = SkillFields.SplitValidateAgainst(fields);

        return new SkillDocument(
            name,
            description,
            string.IsNullOrWhiteSpace(version) ? DefaultVersion : version,
            triggerWhen,
            split.Paths,
            split.Refs,
            extracted.Body);
    }
}

/// <summary>
/// Result of <see cref="SkillFields.SplitValidateAgainst"/> — the
/// mixed-shape <c>validate_against</c> frontmatter split into the plain
/// string list and the SourceRef-shaped object list, so the brain can
/// iterate each side without re-parsing.
/// </summary>
/// <param name="Paths">Plain string paths/URLs from <c>validate_against</c>.</param>
/// <param name="Refs">SourceRef-shaped objects from <c>validate_against</c>.</param>
file sealed record ValidateAgainstSplit(
    IReadOnlyList<string> Paths,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Refs);

/// <summary>One frontmatter field: a scalar, a list of items, or a flow-mapped object — exactly one of the three is set.</summary>
/// <param name="Scalar">Set when the field is a scalar value.</param>
/// <param name="Items">Set when the field is a list of items (each scalar or flow-mapped object).</param>
/// <param name="Object">Set when the field is a single flow-mapped object value.</param>
file sealed record FrontmatterField(
    string? Scalar,
    IReadOnlyList<FrontmatterListItem> Items,
    IReadOnlyDictionary<string, string>? Object);

/// <summary>One item of a flow or block list — either a scalar string or a flow-mapped object.</summary>
/// <param name="Scalar">Set when the item is a plain string.</param>
/// <param name="Object">Set when the item is a <c>{ k: v }</c> object.</param>
file sealed record FrontmatterListItem(
    string? Scalar,
    IReadOnlyDictionary<string, string>? Object)
{
    public bool IsScalar => Scalar is not null;

    public string AsScalar()
    {
        return Scalar ?? throw new InvalidOperationException("frontmatter list item is an object, not a scalar");
    }
}

/// <summary>Result of scanning consecutive block-list item lines: the items and where scanning stopped.</summary>
/// <param name="Items">The block-list items parsed in order.</param>
/// <param name="NextIndex">Index in the lines array immediately past the last block-list item.</param>
file sealed record BlockListScan(IReadOnlyList<FrontmatterListItem> Items, int NextIndex);

/// <summary>
/// Key-scan + value classification for skill documents: every field is
/// either a scalar, a list of items (each scalar or flow-mapped object),
/// or a single flow-mapped object. Lives next to the parser that uses it;
/// the shared fence / pattern / strip / split helpers live in
/// <see cref="YamlishFrontmatter"/>.
/// </summary>
file static class SkillFields
{
    public static Dictionary<string, FrontmatterField> Parse(string yaml)
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
                if (blockList.Items.Count > 0)
                {
                    result[key] = new FrontmatterField(null, blockList.Items, null);
                    index = blockList.NextIndex;
                }

                continue;
            }

            var flowMatch = YamlishFrontmatter.FlowListPattern().Match(value);
            if (flowMatch.Success)
            {
                result[key] = new FrontmatterField(null, SplitFlowList(flowMatch.Groups[1].Value), null);
                continue;
            }

            var objectMatch = YamlishFrontmatter.FlowObjectPattern().Match(value);
            if (objectMatch.Success)
            {
                var parsedObject = ParseFlowObject(objectMatch.Groups[1].Value);
                if (parsedObject is not null)
                {
                    result[key] = new FrontmatterField(null, [], parsedObject);
                }

                continue;
            }

            result[key] = new FrontmatterField(YamlishFrontmatter.StripQuotes(value), [], null);
        }

        return result;
    }

    public static string? Scalar(Dictionary<string, FrontmatterField> fields, string key)
    {
        return fields.TryGetValue(key, out var field) ? field.Scalar : null;
    }

    /// <summary>
    /// Returns the <c>trigger_when</c>-style list — a scalar value degrades
    /// to a single-item list, the same way the older profile parser does for
    /// <c>allowedTools</c>.
    /// </summary>
    /// <param name="fields"></param>
    /// <param name="key"></param>
    public static IReadOnlyList<string> ScalarList(Dictionary<string, FrontmatterField> fields, string key)
    {
        if (!fields.TryGetValue(key, out var field))
        {
            return [];
        }

        if (field.Scalar is { } scalar)
        {
            return [scalar];
        }

        var result = new List<string>(field.Items.Count);
        foreach (var item in field.Items)
        {
            if (item.IsScalar)
            {
                result.Add(item.AsScalar());
            }
        }

        return result;
    }

    /// <summary>
    /// Splits the mixed <c>validate_against</c> field into plain string paths
    /// (or URLs) and SourceRef-shaped objects. A scalar value degrades to a
    /// single-item list before the split; a single flow-mapped object
    /// (the field is itself a <c>{ k: v }</c> value) becomes a single Ref.
    /// </summary>
    public static ValidateAgainstSplit SplitValidateAgainst(Dictionary<string, FrontmatterField> fields)
    {
        if (!fields.TryGetValue(SkillDocumentParser.ValidateAgainstKey, out var field))
        {
            return new ValidateAgainstSplit([], []);
        }

        // The field can be a scalar, a single object, or a list of
        // items. A single object becomes a one-element Ref list; a
        // scalar becomes a one-item Items list.
        List<FrontmatterListItem> items = field switch
        {
            { Scalar: { } scalar } => [new(scalar, null)],
            { Object: { } single } => [new(null, single)],
            _ => [.. field.Items],
        };

        var paths = new List<string>();
        var refs = new List<IReadOnlyDictionary<string, string>>();
        foreach (var item in items)
        {
            if (item.IsScalar)
            {
                paths.Add(item.AsScalar());
            }
            else if (item.Object is not null)
            {
                refs.Add(item.Object);
            }
        }

        return new ValidateAgainstSplit(paths, refs);
    }

    /// <summary>
    /// Scans consecutive block-list item lines: each starts with whitespace
    /// + dash, and the body is either a plain scalar or a single-line
    /// flow-mapped object <c>{ k: v }</c>.
    /// </summary>
    public static BlockListScan TakeBlockListItems(string[] lines, int startIndex)
    {
        var items = new List<FrontmatterListItem>();
        var index = startIndex;

        while (index < lines.Length)
        {
            var itemMatch = YamlishFrontmatter.BlockItemPattern().Match(lines[index]);
            if (!itemMatch.Success)
            {
                break;
            }

            var body = itemMatch.Groups[1].Value.Trim();
            if (TryParseFlowObjectBody(body) is { } parsedObject)
            {
                items.Add(new FrontmatterListItem(null, parsedObject));
            }
            else
            {
                items.Add(new FrontmatterListItem(YamlishFrontmatter.StripQuotes(body), null));
            }

            index++;
        }

        return new BlockListScan(items, index);
    }

    /// <summary>
    /// Splits a flow-list body (the content between the outer <c>[</c> and
    /// <c>]</c>) on top-level commas, then classifies each piece as a
    /// scalar or a single-line flow-mapped object. Delegates the comma
    /// tracking to <see cref="YamlishFrontmatter.SplitTopLevelCommas"/> —
    /// the shared helper tracks quotes, braces and brackets, so an inner
    /// comma inside a <c>{ kind: ..., id: ... }</c> object does not split
    /// the surrounding list.
    /// </summary>
    public static IReadOnlyList<FrontmatterListItem> SplitFlowList(string content)
    {
        var pieces = YamlishFrontmatter.SplitTopLevelCommas(content);
        var items = new List<FrontmatterListItem>(pieces.Count);
        foreach (var piece in pieces)
        {
            AppendFlowListItem(items, piece);
        }

        return items;
    }

    /// <summary>
    /// Classifies one flow-list item — either a single-line flow-mapped
    /// object (delegated to <see cref="ParseFlowObject"/>) or a scalar
    /// with surrounding quotes stripped.
    /// </summary>
    /// <param name="items">The list to append the classified item to.</param>
    /// <param name="raw">One trimmed piece produced by <see cref="YamlishFrontmatter.SplitTopLevelCommas"/>.</param>
    public static void AppendFlowListItem(List<FrontmatterListItem> items, string raw)
    {
        if (raw.Length == 0)
        {
            return;
        }

        if (TryParseFlowObjectBody(raw) is { } parsedObject)
        {
            items.Add(new FrontmatterListItem(null, parsedObject));
            return;
        }

        items.Add(new FrontmatterListItem(YamlishFrontmatter.StripQuotes(raw), null));
    }

    /// <summary>
    /// Single-line flow-mapped object probe: returns
    /// <c>ParseFlowObject(body[1..^1])</c> when the body is wrapped in a
    /// matched pair of <c>{</c>/<c>}</c> braces; otherwise null so the
    /// caller falls back to treating it as a scalar.
    /// </summary>
    /// <param name="raw">The trimmed body of a flow-list item or block-list item.</param>
    public static IReadOnlyDictionary<string, string>? TryParseFlowObjectBody(string raw)
    {
        return raw is { Length: > 1 } body && body[0] == '{' && body[^1] == '}'
            ? ParseFlowObject(body[1..^1])
            : null;
    }

    /// <summary>
    /// Parses a single-level inline object body — <c>kind: knowledge, id: "x@v1"</c>.
    /// Quoted values are unquoted. Missing colons, empty values, or empty objects
    /// yield null; the caller falls back to treating the body as a scalar.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ParseFlowObject(string body)
    {
        var entries = YamlishFrontmatter.SplitTopLevelCommas(body);
        if (entries.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(entries.Count);
        foreach (var entry in entries)
        {
            var colonIndex = entry.IndexOf(':');
            if (colonIndex < 0)
            {
                return null;
            }

            var key = entry[..colonIndex].Trim();
            var value = entry[(colonIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
            {
                return null;
            }

            result[key] = YamlishFrontmatter.StripQuotes(value);
        }

        return result.Count > 0 ? result : null;
    }
}
