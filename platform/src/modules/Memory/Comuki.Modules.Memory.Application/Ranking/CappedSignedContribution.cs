namespace Comuki.Modules.Memory.Application.Ranking;

/// <summary>
/// Capped signed contribution: a single number that the learning loop
/// updates when a project under a candidate rule succeeds or fails.
/// The function clamps the running total to the interval
/// <c>[-cap, cap]</c> so a long-running project's first week of
/// green builds cannot make the candidate's evidence unboundedly
/// optimistic — symmetric clamping treats success and failure equally.
/// </summary>
public static class CappedSignedContribution
{
    /// <summary>Default cap for the running contribution. Five in either direction is enough to encode a clear pattern without rewarding infinite evidence.</summary>
    public const int DefaultCap = 5;

    /// <summary>
    /// Applies <paramref name="delta" /> to <paramref name="current" /> and clamps the result
    /// to the symmetric interval <c>[-<paramref name="cap" />, +<paramref name="cap" />]</c>.
    /// A null <paramref name="cap" /> falls back to <see cref="DefaultCap" />.
    /// </summary>
    /// <param name="current">Running total before the update; may be negative.</param>
    /// <param name="delta">Signed increment to apply (positive on success, negative on failure).</param>
    /// <param name="cap">Symmetric clamp; null falls back to <see cref="DefaultCap" />.</param>
    public static int Apply(int current, int delta, int? cap = null)
    {
        var bound = Math.Abs(cap ?? DefaultCap);
        return Math.Clamp(current + delta, -bound, bound);
    }
}
