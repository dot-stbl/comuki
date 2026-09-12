namespace Comuki.Host.Cli.Doctor;

/// <summary>Outcome of one <c>comuki doctor</c> check (issue #56 §1.2).</summary>
public enum DoctorCheckStatus
{
    /// <summary>The check passed.</summary>
    Ok,

    /// <summary>Suspicious but not fatal (deprecated fallback, dev default outside Production).</summary>
    Warn,

    /// <summary>The check failed — the exit code becomes 1.</summary>
    Fail,
}
