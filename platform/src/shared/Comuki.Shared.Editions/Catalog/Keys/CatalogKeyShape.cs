namespace Comuki.Shared.Editions.Catalog.Keys;

/// <summary>
/// Shared well-formedness rule for <see cref="FeatureKey"/> and
/// <see cref="LimitKey"/>: a lowercase dash-case slug (e.g.
/// <c>multi-repo</c>), optionally two such slugs joined by one colon
/// (<c>resource:action</c>, the <c>PermissionKey</c> shape).
/// <para>
/// Issue #164's design.md describes the key shape as "same well-formedness
/// as PermissionKey" (colon-separated resource:action), but every concrete
/// example in specs/editions/spec.md's scenarios (<c>multi-repo</c>,
/// <c>enterprise-sso</c>, <c>white-label</c>, <c>background-llm-watchers</c>)
/// is a single dash-case slug with no colon. This shape accepts both: the
/// single-segment case (what every current catalog entry uses) and the
/// optional resource:action case (kept for a future key that wants it) —
/// resolving the spec's internal inconsistency in favor of its own worked
/// examples rather than its prose summary.
/// </para>
/// </summary>
internal static class CatalogKeyShape
{
    private const int MaxSegmentLength = 64;

    /// <summary>Whether <paramref name="value"/> is a well-formed feature/limit key.</summary>
    public static bool IsWellFormed(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var span = value.AsSpan();
        var separator = span.IndexOf(':');
        if (separator < 0)
        {
            return IsSegment(span);
        }

        var rest = span[(separator + 1)..];
        return separator >= 1 && rest.IndexOf(':') is -1 && IsSegment(span[..separator]) && IsSegment(rest);
    }

    private static bool IsSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length is < 1 or > MaxSegmentLength || !char.IsAsciiLetterLower(segment[0]))
        {
            return false;
        }

        foreach (var character in segment)
        {
            var isAllowed = char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
