namespace Comuki.Modules.Identity.Domain.Permissions;

/// <summary>
/// A permission key — the action axis of the authorization model. An
/// open string vocabulary of <c>resource:action</c> keys declared in code
/// (<see cref="Permissions"/>); roles map onto keys via <see cref="Roles.RoleMatrix"/>.
/// A string, not an enum, so the vocabulary can grow without breaking
/// stored assignments; the startup validator is what a string costs —
/// an undeclared key fails the boot, not the request.
/// </summary>
/// <param name="Value">The key, e.g. <c>run:stop</c>.</param>
public readonly record struct PermissionKey(string Value)
{
    private const int MaxSegmentLength = 32;

    /// <summary>
    /// Whether the value has the <c>resource:action</c> shape: exactly one
    /// colon, both segments 1–32 chars of lowercase letters, digits and
    /// dashes, starting with a letter.
    /// </summary>
    /// <param name="value"></param>
    public static bool IsWellFormed(string value)
    {
        var body = value.AsSpan();
        var separator = body.IndexOf(':');

        return separator is >= 1
            && body[(separator + 1)..].IndexOf(':') is -1
            && IsSegment(body[..separator])
            && IsSegment(body[(separator + 1)..]);
    }

    /// <summary>
    /// Parses a well-formed key; anything else throws. For untrusted input
    /// check <see cref="IsWellFormed"/> first.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="FormatException">The value is not a well-formed key.</exception>
    public static PermissionKey Parse(string value)
    {
        return IsWellFormed(value)
            ? new PermissionKey(value)
            : throw new FormatException($"'{value}' is not a well-formed permission key (expected resource:action)");
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value;
    }

    private static bool IsSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length is < 1 or > MaxSegmentLength || !char.IsAsciiLetterLower(segment[0]))
        {
            return false;
        }

        foreach (var character in segment)
        {
            var isAllowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character == '-';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }
}
