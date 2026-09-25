namespace Comuki.Shared.Editions.Licensing.Ed25519;

/// <summary>
/// The raw JSON DTO both signer and provider (de)serialize. Not public
/// — it is an implementation detail of the wire format, identified by
/// its camelCase property names via <c>JsonSerializerOptions.Web</c>.
/// Optional fields (<c>notBefore</c>, <c>seats</c>, <c>features</c>,
/// <c>limits</c>) are nullable so an absent JSON value deserialises to
/// <c>null</c> rather than throwing.
/// </summary>
internal sealed record LicensePayload
{
    /// <summary>Licensed organisation name.</summary>
    public string? Org { get; init; }

    /// <summary>Edition tier code (<c>"community"</c>, <c>"team"</c>, …).</summary>
    public string? Edition { get; init; }

    /// <summary>Optional seat count (informational only).</summary>
    public int? Seats { get; init; }

    /// <summary>Optional validity start instant; ISO-8601 string on the wire.</summary>
    public DateTimeOffset? NotBefore { get; init; }

    /// <summary>End of the validity window; ISO-8601 string on the wire.</summary>
    public DateTimeOffset? Expiry { get; init; }

    /// <summary>Wire form of <c>LicenseMode</c> — <c>"implicit-by-rank"</c> or <c>"explicit-allowlist"</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Wire form of <c>LicenseAudience</c>. Only emitted by the signer
    /// when the grant names <c>Dev</c> — every pre-audience payload
    /// bytes stays byte-identical so historical production tokens
    /// continue to verify. The verifier maps a missing field to
    /// <c>Production</c>.
    /// </summary>
    public string? Audience { get; init; }

    /// <summary>Optional capability keys (raw strings, not yet resolved against the catalog).</summary>
    public IReadOnlyCollection<string>? Features { get; init; }

    /// <summary>Optional raw quota caps.</summary>
    public IReadOnlyDictionary<string, int>? Limits { get; init; }
}
