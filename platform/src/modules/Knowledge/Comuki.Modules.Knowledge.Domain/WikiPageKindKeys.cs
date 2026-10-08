namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// Stable wire keys for <see cref="WikiPageKind"/> — the database column
/// values and the markdown frontmatter <c>kind</c> values use these
/// strings.
/// </summary>
public static class WikiPageKindKeys
{
    /// <summary>Key of <see cref="WikiPageKind.Glossary"/>.</summary>
    public const string Glossary = "glossary";

    /// <summary>Key of <see cref="WikiPageKind.HowTo"/>.</summary>
    public const string HowTo = "how-to";

    /// <summary>Key of <see cref="WikiPageKind.DecisionRecord"/>.</summary>
    public const string DecisionRecord = "decision-record";

    /// <summary>Key of <see cref="WikiPageKind.Incident"/>.</summary>
    public const string Incident = "incident";

    /// <summary>Key of <see cref="WikiPageKind.Reference"/>.</summary>
    public const string Reference = "reference";

    /// <summary>Maps a kind to its wire key.</summary>
    public static string Key(WikiPageKind kind)
    {
        return kind switch
        {
            WikiPageKind.Glossary => Glossary,
            WikiPageKind.HowTo => HowTo,
            WikiPageKind.DecisionRecord => DecisionRecord,
            WikiPageKind.Incident => Incident,
            WikiPageKind.Reference => Reference,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Parses a wire key; null when unknown.</summary>
    public static WikiPageKind? Parse(string key)
    {
        return key switch
        {
            Glossary => WikiPageKind.Glossary,
            HowTo => WikiPageKind.HowTo,
            DecisionRecord => WikiPageKind.DecisionRecord,
            Incident => WikiPageKind.Incident,
            Reference => WikiPageKind.Reference,
            _ => null,
        };
    }

    /// <summary>Parses a wire key or throws — used by the EF converter path (expression trees cannot inline throws).</summary>
    /// <exception cref="InvalidOperationException">The key is unknown.</exception>
    public static WikiPageKind ParseRequired(string key)
    {
        return Parse(key) ?? throw new InvalidOperationException($"unknown wiki page kind key '{key}'");
    }
}
