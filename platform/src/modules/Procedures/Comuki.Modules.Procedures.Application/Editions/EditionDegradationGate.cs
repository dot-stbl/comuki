namespace Comuki.Modules.Procedures.Application.Editions;

/// <summary>
/// Task 6.2: expiry degradation is read-only — definitions and replay
/// remain visible, pins and traces stay queryable, but no new
/// compilations or runs start. This class checks and enforces that.
/// </summary>
public static class EditionDegradationGate
{
    /// <summary>
    /// Refuses a new compilation when the edition is expired. Read
    /// operations (GetAsync, ListByProcedureAsync, trace queries) are
    /// NOT gated — only writes are.
    /// </summary>
    /// <param name="isExpired">Whether the effective edition has expired.</param>
    public static void CheckCanCompile(bool isExpired)
    {
        if (isExpired)
        {
            throw new ProcedureEditionsException(
                ProcedureEditionsException.EditionExpiredReadOnly,
                "The effective edition has expired. Definitions and replay remain visible; new compilations and runs are refused. Renew the license to resume.");
        }
    }

    /// <summary>
    /// Refuses a new run admission when the edition is expired. Existing
    /// pinned runs continue to completion — only NEW admissions are refused.
    /// </summary>
    /// <param name="isExpired">Whether the effective edition has expired.</param>
    public static void CheckCanAdmit(bool isExpired)
    {
        if (isExpired)
        {
            throw new ProcedureEditionsException(
                ProcedureEditionsException.EditionExpiredReadOnly,
                "The effective edition has expired. Pins and traces remain queryable; new run admissions are refused. Renew the license to resume.");
        }
    }
}
