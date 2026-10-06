using System.Text;
using System.Text.Json;
using Comuki.Host.Translator.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Comuki.Host.Translator.Unit.Runtime;

/// <summary>
/// Wire test for <see cref="PiRpcTurnInputWriter"/>: closes the C3
/// follow-up tail of wave 2 (revised canon verdict — the
/// stdin-side JSON-RPC write is the production path
/// <c>openspec/changes/add-orchestra/spike-1b-report.md</c>
/// documents). Asserts that
/// <see cref="PiRpcTurnInputWriter.TryWriteSteer"/> and
/// <see cref="PiRpcTurnInputWriter.TryWriteFollowUp"/> write exactly
/// the wire shape pi parses: one JSON object per LF, three
/// single-word keys (<c>type</c>, <c>id</c>, <c>message</c>),
/// LF terminator (no CR). The session-level shape (one wave per
/// inbound command) is covered by
/// <c>TestFakeHarnessSessionShould</c>; this test covers the wire
/// shape itself.
/// </summary>
public sealed class PiRpcTurnInputWriterShould
{
    [Fact(DisplayName = "Given a writer on a MemoryStream, when TryWriteSteer is called, then one LF-terminated JSON line is written with type=steer, id, message")]
    public void TryWriteSteerWritesExpectedWireShape()
    {
        using var memoryStream = new MemoryStream();
        var writer = new PiRpcTurnInputWriter(memoryStream, NullLogger<PiRpcTurnInputWriter>.Instance);

        var accepted = writer.TryWriteSteer(turnId: "turn-1", text: "actually do this instead");

        accepted.ShouldBeTrue("TryWriteSteer returns false only on a closed harness stream");
        var line = ReadSingleLine(memoryStream);
        var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        root.ValueKind.ShouldBe(JsonValueKind.Object);
        AssertExactlyThreeFields(root, expectedType: "steer");
        root.GetProperty("id").GetString().ShouldBe("turn-1");
        root.GetProperty("message").GetString().ShouldBe("actually do this instead");
    }

    [Fact(DisplayName = "Given a writer on a MemoryStream, when TryWriteFollowUp is called, then one LF-terminated JSON line is written with type=follow_up, id, message")]
    public void TryWriteFollowUpWritesExpectedWireShape()
    {
        using var memoryStream = new MemoryStream();
        var writer = new PiRpcTurnInputWriter(memoryStream, NullLogger<PiRpcTurnInputWriter>.Instance);

        var accepted = writer.TryWriteFollowUp(turnId: "f-2", text: "follow-up body");

        accepted.ShouldBeTrue("TryWriteFollowUp returns false only on a closed harness stream");
        var line = ReadSingleLine(memoryStream);
        var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        root.ValueKind.ShouldBe(JsonValueKind.Object);
        AssertExactlyThreeFields(root, expectedType: "follow_up");
        root.GetProperty("id").GetString().ShouldBe("f-2");
        root.GetProperty("message").GetString().ShouldBe("follow-up body");
    }

    /// <summary>
    /// Reads the LF-terminated line off <paramref name="memoryStream"/>
    /// and asserts the line is the only one written (no extra payload,
    /// no CR before the LF — pi's wire parser does not strip a CR).
    /// </summary>
    /// <param name="memoryStream">Stream the test fed the writer.</param>
    /// <returns>The single JSON object body (without the LF terminator).</returns>
    private static string ReadSingleLine(MemoryStream memoryStream)
    {
        memoryStream.Position = 0;
        var bytes = memoryStream.ToArray();
        bytes.ShouldNotBeEmpty("TryWrite* returned true but the stream was empty");
        // LF terminator on the last byte — the writer's StreamWriter is
        // configured with NewLine = "\n"; a CR would mean pi wouldn't
        // parse the line cleanly.
        bytes[^1].ShouldBe((byte)'\n', "the line must end with LF (\\n), not be CR-terminated or unterminated");
        // No embedded CRs — pi's wire parser tokenises on LF; a CR in
        // the middle of the body would slip into the type/id/message
        // string and confuse the parser.
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            bytes[i].ShouldNotBe((byte)'\r', "the body must not contain CR; only the terminator is LF");
        }

        var text = Encoding.UTF8.GetString(bytes);
        // Exactly one command on the stream — split on LF, one
        // non-empty entry before the trailing empty (the post-LF
        // terminator). Multiple non-empty entries mean the writer
        // slipped a delimiter.
        var nonEmpty = text.Split('\n').Where(static part => part.Length > 0).ToArray();
        nonEmpty.Length.ShouldBe(1, "exactly one command on the stream");
        return nonEmpty[0];
    }

    /// <summary>
    /// Asserts the JSON root carries exactly three single-word keys
    /// (<c>type</c>, <c>id</c>, <c>message</c>) and that
    /// <c>type</c> matches <paramref name="expectedType"/>.
    /// </summary>
    /// <param name="root">Parsed JSON object.</param>
    /// <param name="expectedType">The wire shape's <c>type</c> field.</param>
    private static void AssertExactlyThreeFields(JsonElement root, string expectedType)
    {
        var fields = root.EnumerateObject()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        fields.ShouldBe(["id", "message", "type"], "pi's wire shape is exactly { type, id, message } — no extras");
        root.GetProperty("type").GetString().ShouldBe(expectedType);
    }
}
