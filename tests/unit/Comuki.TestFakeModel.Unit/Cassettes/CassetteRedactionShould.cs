using System.Text.Json;
using Comuki.TestFakeModel.Cassettes.Redaction;
using Shouldly;
using Xunit;

namespace Comuki.TestFakeModel.Unit.Cassettes;

/// <summary>
/// <see cref="CassetteRedactor"/>: the allowlisted-field pass-through, the
/// secret-pattern value scrub, the denylisted-field-name forced redaction,
/// and — the fail-closed guarantee design.md requires — refusal to write
/// a response carrying a field the redactor doesn't recognize.
/// </summary>
public sealed class CassetteRedactionShould
{
    [Fact(DisplayName = "Given an allowlisted response shape with no secrets, when Redact is called, then every field passes through unchanged")]
    public void PassThroughAllowlistedFieldsUnchanged()
    {
        using var source = JsonDocument.Parse(
            /*lang=json,strict*/ """
            {"id":"msg_x_1","type":"message","role":"assistant","model":"claude-sonnet-5","content":[{"type":"text","text":"hello"}],"stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":5}}
            """);

        var redacted = CassetteRedactor.Redact(source.RootElement);

        redacted.GetProperty("id").GetString().ShouldBe("msg_x_1");
        redacted.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("hello");
        redacted.GetProperty("usage").GetProperty("input_tokens").GetInt32().ShouldBe(10);
    }

    [Theory(DisplayName = "Given a string value shaped like a known secret, when Redact is called, then it's replaced with [redacted]")]
    [InlineData("sk-abcdefghijklmnop1234")]
    [InlineData("sk-ant-abcdefghijklmnop1234")]
    [InlineData("ck_abcdef123456")]
    [InlineData("Bearer some-token-value")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz012345")]
    public void RedactSecretShapedStringValues(string secretValue)
    {
        using var source = JsonDocument.Parse(
            /*lang=json,strict*/ $$"""{"type":"text","text":{{JsonSerializer.Serialize(secretValue)}}}""");

        var redacted = CassetteRedactor.Redact(source.RootElement);

        redacted.GetProperty("text").GetString().ShouldBe("[redacted]");
    }

    [Fact(DisplayName = "Given a field name on the redaction denylist, when Redact is called, then its value is forced to [redacted] even though the name itself isn't on the allowlist")]
    public void RedactDenylistedFieldNamesOutright()
    {
        using var source = JsonDocument.Parse(
            /*lang=json,strict*/ """
            {"type":"text","text":"ok","ANTHROPIC_AUTH_TOKEN":"whatever-value-was-here"}
            """);

        var redacted = CassetteRedactor.Redact(source.RootElement);

        redacted.GetProperty("ANTHROPIC_AUTH_TOKEN").GetString().ShouldBe("[redacted]");
    }

    [Fact(DisplayName = "Given a response field not on the allowlist, when Redact is called, then it refuses to write (fail-closed), not pass the field through")]
    public void RefuseToWriteAnUnclassifiedField()
    {
        using var source = JsonDocument.Parse(
            /*lang=json,strict*/ """
            {"type":"text","text":"ok","a_field_no_wire_shape_declares":"mystery-value"}
            """);

        var exception = Should.Throw<CassetteRedactionRefusedException>(() => CassetteRedactor.Redact(source.RootElement));
        exception.Message.ShouldContain("a_field_no_wire_shape_declares");
    }

    [Fact(DisplayName = "Given a nested tool_use.input object carrying a leaked secret value, when Redact is called, then the nested value is redacted too (defense in depth)")]
    public void RedactSecretsNestedInsideAllowlistedObjects()
    {
        using var source = JsonDocument.Parse(
            /*lang=json,strict*/ """
            {"type":"tool_use","name":"curl","input":{"header":"Bearer abcdef123456"}}
            """);

        var redacted = CassetteRedactor.Redact(source.RootElement);

        redacted.GetProperty("input").GetProperty("header").GetString().ShouldBe("[redacted]");
    }
}
