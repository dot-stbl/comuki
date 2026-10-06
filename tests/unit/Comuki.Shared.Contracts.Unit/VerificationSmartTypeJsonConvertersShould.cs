using System.Text.Json;
using Comuki.Shared.Contracts.Verification;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Contracts.Unit;

/// <summary>
/// Round-trip contract for the verification smart-type JSON converters
/// (add-orchestra §3 — Coda, <c>verification/spec.md</c>). The wire
/// form is the lowercase value <see cref="GateVerdict.Value"/> /
/// <see cref="GateEvidenceKind.Value"/> exposes; <c>FromWire</c> must
/// collapse an unknown wire value to <see cref="GateVerdict.Unspecified"/>
/// / <see cref="GateEvidenceKind.Other"/> on read so a freshly-loaded
/// row never throws on a kind the catalogue has not seen yet.
/// </summary>
public sealed class VerificationSmartTypeJsonConvertersShould
{
    [Fact(DisplayName = "Given GateVerdict.Pending, when round-tripped through the converter, then reads back Pending")]
    public void GateVerdictPendingRoundTrips()
    {
        var source = GateVerdict.Pending;
        var json = JsonSerializer.Serialize(source, verificationJsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateVerdict>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateVerdict.Pending);
    }

    [Fact(DisplayName = "Given GateVerdict.Passed, when round-tripped through the converter, then reads back Passed")]
    public void GateVerdictPassedRoundTrips()
    {
        var source = GateVerdict.Passed;
        var json = JsonSerializer.Serialize(source, verificationJsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateVerdict>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateVerdict.Passed);
    }

    [Fact(DisplayName = "Given GateVerdict.Failed, when round-tripped through the converter, then reads back Failed")]
    public void GateVerdictFailedRoundTrips()
    {
        var source = GateVerdict.Failed;
        var json = JsonSerializer.Serialize(source, verificationJsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateVerdict>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateVerdict.Failed);
    }

    [Fact(DisplayName = "Given an unknown GateVerdict wire value, when deserialized, then collapses to Unspecified")]
    public void GateVerdictUnknownCollapsesToUnspecified()
    {
        var json = "\"some-typo\"";
        var roundTripped = JsonSerializer.Deserialize<GateVerdict>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateVerdict.Unspecified);
    }

    [Fact(DisplayName = "Given a null GateVerdict payload, when deserialized, then yields the default struct (Unspecified)")]
    public void GateVerdictNullYieldsDefault()
    {
        var json = "null";
        var roundTripped = JsonSerializer.Deserialize<GateVerdict>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateVerdict.Unspecified);
    }

    [Fact(DisplayName = "Given GateEvidenceKind.Cmdiff, when round-tripped through the converter, then reads back Cmdiff")]
    public void GateEvidenceKindCmdiffRoundTrips()
    {
        var source = GateEvidenceKind.Cmdiff;
        var json = JsonSerializer.Serialize(source, verificationJsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateEvidenceKind>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateEvidenceKind.Cmdiff);
    }

    [Fact(DisplayName = "Given GateEvidenceKind.Stdout, when round-tripped through the converter, then reads back Stdout")]
    public void GateEvidenceKindStdoutRoundTrips()
    {
        var source = GateEvidenceKind.Stdout;
        var json = JsonSerializer.Serialize(source, verificationJsonOptions);
        var roundTripped = JsonSerializer.Deserialize<GateEvidenceKind>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateEvidenceKind.Stdout);
    }

    [Fact(DisplayName = "Given a provider-specific GateEvidenceKind wire value, when deserialized, then collapses to Other")]
    public void GateEvidenceKindUnknownCollapsesToOther()
    {
        var json = "\"verify:provider-specific-kind\"";
        var roundTripped = JsonSerializer.Deserialize<GateEvidenceKind>(json, verificationJsonOptions);

        roundTripped.ShouldBe(GateEvidenceKind.Other);
    }

    [Fact(DisplayName = "Given a non-string GateEvidenceKind payload, when deserialized, then throws JsonException with a typed refusal")]
    public void GateEvidenceKindNonStringThrowsTypedException()
    {
        var json = "42";

        var exception = Should.Throw<JsonException>(
            () => JsonSerializer.Deserialize<GateEvidenceKind>(json, verificationJsonOptions));

        exception.Message.ShouldContain("GateEvidenceKind");
    }

    private static readonly JsonSerializerOptions verificationJsonOptions = new()
    {
        Converters =
        {
            new GateEvidenceKindJsonConverter(),
            new GateVerdictJsonConverter(),
        },
    };
}
