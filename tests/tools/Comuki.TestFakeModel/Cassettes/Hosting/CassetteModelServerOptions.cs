namespace Comuki.TestFakeModel.Cassettes.Hosting;

/// <summary>Construction options for <see cref="CassetteModelServer"/>.</summary>
public sealed class CassetteModelServerOptions
{
    /// <summary>Which mode to run — <see cref="CassetteModelMode.Replay"/> or <see cref="CassetteModelMode.Record"/>.</summary>
    public required CassetteModelMode Mode { get; init; }

    /// <summary>
    /// The cassette file path. <see cref="CassetteModelMode.Replay"/> reads
    /// it (must exist). <see cref="CassetteModelMode.Record"/> appends to
    /// it, creating it (with <see cref="Scenario"/>/<see cref="RecordedAgainst"/>
    /// as its header) if it doesn't exist yet.
    /// </summary>
    public required string CassettePath { get; init; }

    /// <summary>New-cassette header field (record mode only) — ignored when the cassette already exists, since its own header wins.</summary>
    public string Scenario { get; init; } = "fake";

    /// <summary>New-cassette header field (record mode only) — the model id recorded against, e.g. <c>claude-sonnet-5</c>.</summary>
    public string RecordedAgainst { get; init; } = "unknown";

    /// <summary>Required when <see cref="Mode"/> is <see cref="CassetteModelMode.Record"/> — where <c>Recording.CassetteUpstreamForwarder</c> forwards each request.</summary>
    public Uri? UpstreamBaseUrl { get; init; }

    /// <summary>Loopback port to bind. <c>null</c> asks the OS for an ephemeral free port — see <c>Hosting.FakeModelServerOptions.Port</c>.</summary>
    public int? Port { get; init; }

    /// <summary>Address Kestrel binds — see <c>Hosting.FakeModelServerOptions.BindAddress</c>.</summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>The clock <see cref="CassetteModelMode.Record"/> stamps each newly-appended exchange's cassette header with.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
