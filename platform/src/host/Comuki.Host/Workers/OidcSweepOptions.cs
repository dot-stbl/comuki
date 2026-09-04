using System.ComponentModel.DataAnnotations;

namespace Comuki.Host.Workers;

/// <summary>
/// Settings for the OIDC state sweep hosted service. The host deletes
/// rows in <c>identity.oidc_states</c> past <see cref="StateTtl"/> on a
/// fixed interval — the OIDC code-flow's start handler issues state rows
/// with a 5-minute TTL, so the sweep keeps the table bounded without
/// sitting on a hot delete loop.
/// </summary>
public sealed class OidcSweepOptions
{
    /// <summary>Configuration section: <c>Host:OidcSweep</c>.</summary>
    public const string SectionName = "Host:OidcSweep";

    /// <summary>Master switch — false disables the hosted service entirely.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Interval between sweep cycles. Five minutes is the documented
    /// default — long enough that the table stays quiet under low load,
    /// short enough that a state row is gone before the TTL expires by
    /// any meaningful margin.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00")]
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// State-row TTL the sweeper enforces — rows past <c>ExpiresAt</c> +
    /// this grace are deleted. Defaults to the OIDC start handler's own
    /// 5-minute TTL so the sweep is a no-op on fresh rows and only
    /// catches the abandoned ones (operator closed the tab, IdP call
    /// dropped, etc.).
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "00:30:00")]
    public TimeSpan StateTtl { get; init; } = TimeSpan.FromMinutes(5);
}
