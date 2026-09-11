namespace Comuki.Host.Cli.Doctor;

/// <summary>
/// One line of the <c>comuki doctor</c> checklist (issue #56 §1.2):
/// <c>ok    config      path=/etc/comuki/config.toml</c> — status label
/// padded to five, the check name padded to eighteen, then the flat
/// <c>key=val</c> detail.
/// </summary>
/// <param name="Name">Check name (config, environment, database, secrets.*, migrations).</param>
/// <param name="Status">Outcome severity.</param>
/// <param name="Detail">Flat detail of the outcome.</param>
public sealed record DoctorCheck(string Name, DoctorCheckStatus Status, string Detail)
{
    /// <summary>Renders the checklist line.</summary>
    public string Render()
    {
        return $"{StatusLabel(Status),-5} {Name,-18} {Detail}";
    }

    /// <summary>The lowercase comuki label of a status.</summary>
    public static string StatusLabel(DoctorCheckStatus status)
    {
        return status switch
        {
            DoctorCheckStatus.Ok => "ok",
            DoctorCheckStatus.Warn => "warn",
            DoctorCheckStatus.Fail => "fail",
            _ => "?",
        };
    }

    /// <summary>Exit code for a completed run: 1 when any check failed, else 0.</summary>
    public static int ExitCode(IReadOnlyList<DoctorCheck> checks)
    {
        return checks.Any(static check => check.Status == DoctorCheckStatus.Fail) ? 1 : 0;
    }
}
