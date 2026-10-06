using System.Text.RegularExpressions;

namespace Comuki.Shared.Kernel;

/// <summary>
/// The shared YAML-subset scanner the control-plane and wiki layers
/// all reach for: <c>---</c>-fenced frontmatter, <c>key: value</c>
/// scalars, <c>[a, b, c]</c> flow lists, <c>- item</c> block lists, and
/// <c>{ k: v }</c> flow-mapped objects. Comments are <c>#</c>-prefixed
/// (same as the TS reader at
/// <c>agents/comuki-agent-core/src/rules/reader.ts</c>).
///
/// Mirrors the TS reader on purpose — the same scalar / flow-list /
/// block-list / flow-object subset, the same tolerance, so the C#
/// host and the worker-side TS readers see identical content. Tags and
/// deeper nesting are ignored: this is the "Yamlish" surface, not a
/// full YAML parser.
/// </summary>
/// <remarks>
/// One place per pattern (this is the only file in the kernel that is
/// allowed to declare GeneratedRegex source-generated regex methods).
/// Consumers compose these patterns with their own per-shape value
/// classification — control-plane docs, wiki pages, and skill
/// documents each give the same scalar / flow-list / object key a
/// different downstream projection.
/// </remarks>
public static partial class YamlishFrontmatter
{
    /// <summary>Frontmatter key line: <c>key: value</c> with an ASCII key.</summary>
    [GeneratedRegex(@"^([A-Za-z][\w.-]*)\s*:\s*(.*)$")]
    public static partial Regex KeyPattern();

    /// <summary>Block-list item line: whitespace, dash, then the item.</summary>
    [GeneratedRegex(@"^\s+-\s+(.*)$")]
    public static partial Regex BlockItemPattern();

    /// <summary>Flow list value: <c>[a, b, c]</c>.</summary>
    [GeneratedRegex(@"^\[(.*)\]$")]
    public static partial Regex FlowListPattern();

    /// <summary>Flow-mapped object value: <c>{ k: v, k2: v2 }</c>.</summary>
    [GeneratedRegex(@"^\{(.*)\}$")]
    public static partial Regex FlowObjectPattern();

    /// <summary>
    /// Extracts the YAML-ish frontmatter from a markdown body. Returns
    /// <c>null</c> when the body has no opening fence, no closing
    /// fence, or an empty frontmatter block.
    /// </summary>
    /// <param name="text">
    /// The full text — frontmatter fences (if any) plus the body that
    /// follows.
    /// </param>
    public static YamlishFrontmatterBlock? Extract(string text)
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
            : new YamlishFrontmatterBlock(
                string.Join('\n', lines[1..endIndex]),
                string.Join('\n', lines[(endIndex + 1)..]));
    }

    /// <summary>
    /// Removes a matched pair of surrounding quotes from a scalar
    /// value: <c>"…"</c> or <c>'…'</c>. A value that is shorter than
    /// two characters, or whose first/last characters do not match,
    /// is returned verbatim — no exception, no trimming.
    /// </summary>
    /// <param name="value">The raw scalar value read from a key: value line.</param>
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

    /// <summary>
    /// Splits a comma-separated list on top-level commas — commas inside
    /// <c>{ ... }</c> braces, <c>[ ... ]</c> brackets, or <c>"…"</c> /
    /// <c>'…'</c> quotes do not split. Depth is tracked at both brace
    /// kinds because flow lists and flow-mapped objects nest freely in
    /// the documented subset (e.g. <c>[{ kind: knowledge, id: "x@v3" }, "a.md"]</c>);
    /// without the brackets half, the inner comma of the object would
    /// be treated as a list separator. When the input carries no
    /// braces or brackets the result is identical to the original
    /// single-quote-or-double-quote-aware split, so callers that only
    /// ever pass a plain comma-separated string (control-plane flow
    /// objects, wiki link entries) keep their existing behaviour.
    /// Trims each piece.
    /// </summary>
    /// <param name="content">
    /// The body to split — the content between the outer <c>{</c> and
    /// <c>}</c> of a flow-mapped object, or the content between the
    /// outer <c>[</c> and <c>]</c> of a flow list, or a plain
    /// comma-separated body.
    /// </param>
    public static IReadOnlyList<string> SplitTopLevelCommas(string content)
    {
        var parts = new List<string>();
        var buffer = new System.Text.StringBuilder();
        var inQuotes = (char?)null;
        var braceDepth = 0;
        var bracketDepth = 0;

        foreach (var character in content)
        {
            if (inQuotes.HasValue)
            {
                buffer.Append(character);
                if (character == inQuotes)
                {
                    inQuotes = null;
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                inQuotes = character;
                buffer.Append(character);
                continue;
            }

            if (character == '{')
            {
                braceDepth++;
            }
            else if (character == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
            }
            else if (character == '[')
            {
                bracketDepth++;
            }
            else if (character == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
            }

            if (character == ',' && braceDepth == 0 && bracketDepth == 0)
            {
                parts.Add(buffer.ToString().Trim());
                buffer.Clear();
                continue;
            }

            buffer.Append(character);
        }

        var tail = buffer.ToString().Trim();
        if (tail.Length > 0)
        {
            parts.Add(tail);
        }

        return parts;
    }
}
