namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>
/// Which of the two cassette-backed modes a <see cref="CassetteModelServer"/>
/// instance runs — smart-type per naming-and-types.md §3 (a closed
/// two-member domain set), not an <c>enum</c>. <c>fake</c> mode isn't a
/// third member here: it's served by the unrelated <c>Hosting.FakeModelServer</c>
/// (see that type's doc for why the two server types stay separate).
/// </summary>
public readonly record struct CassetteModelMode
{
    private readonly string? value;

    private CassetteModelMode(string value)
    {
        this.value = value;
    }

    /// <summary>Lower-case wire-style label — also the <c>--mode=</c>/<c>COMUKI_TESTFAKEMODEL_MODE</c> value <c>Program.cs</c> matches against.</summary>
    public string Value => value ?? "unspecified";

    /// <summary>Default value: <c>default(CassetteModelMode)</c>. Not a runnable mode — resolving it is a caller bug, not a legitimate choice.</summary>
    public static CassetteModelMode Unspecified { get; }

    /// <summary>Serves a committed cassette; refuses (see <c>Replay.CassetteMismatchException</c>) on any drift from the recorded trajectory.</summary>
    public static CassetteModelMode Replay { get; } = new("replay");

    /// <summary>Forwards every request to a real upstream and writes a redacted cassette as it goes.</summary>
    public static CassetteModelMode Record { get; } = new("record");
}
