using System.Text.Json;

namespace Comuki.TestFakeModel.Cassettes.IO;

/// <summary>
/// Appends one exchange to a cassette file, creating it (with header
/// fields) if it doesn't exist yet — the write side <see cref="Recording.CassetteRecordingState"/>
/// calls once per exchange, after <see cref="Redaction.CassetteRedactor"/>
/// has already run (design.md: redaction happens before a byte is
/// written, not as a post-process). Serializes compact, not indented —
/// <c>anti-patterns.md</c> §6 hard-bans an ad-hoc
/// <c>JsonSerializerOptions { WriteIndented = true }</c> outside a DI
/// composition root, and this standalone tool has none to hook into; a
/// future change wiring one up could switch this to a pretty-printed
/// writer for friendlier fixture diffs without changing the format.
/// </summary>
public static class CassetteWriter
{
    /// <summary>Loads <paramref name="path"/> if it exists (else starts a new cassette) and appends <paramref name="redactedExchange"/>.</summary>
    public static async Task AppendExchangeAsync(
        string path,
        string scenario,
        string recordedAgainst,
        DateTimeOffset recordedAt,
        CassetteExchange redactedExchange,
        CancellationToken cancellationToken)
    {
        var cassette = File.Exists(path)
            ? CassetteReader.LoadFromFile(path)
            : new CassetteFile(CassetteFile.CurrentSchemaVersion, scenario, recordedAt, recordedAgainst, []);

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(cassette with { Exchanges = [.. cassette.Exchanges, redactedExchange] }, JsonSerializerOptions.Web),
            cancellationToken);
    }
}
