namespace Comuki.Host.Runs.Models;

/// <summary>
/// Empty body for the run-approve endpoint — the action carries no
/// payload today. Kept as a named type so the controller signature is
/// stable across schema additions (a future revision can grow
/// <c>Note</c> / <c>ProfileKey</c> without changing the route binding).
/// </summary>
public sealed class ApproveRunRequest
{
}
