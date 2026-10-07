using Comuki.Shared.Contracts.Grpc;
using ProtoBuf;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Contracts.Unit;

/// <summary>
/// Wire-format roundtrip for the protobuf-net gRPC contracts that
/// ride the bidi command stream (add-orchestra Phase 1c — B3). A
/// non-trivial <c>IReadOnlyDictionary&lt;TKey,TValue&gt;</c> field
/// would silently lose entries on the protobuf-net roundtrip —
/// protobuf-net materialises concrete <c>Dictionary</c> only — so
/// the contract ships <c>Dictionary&lt;string,string&gt;</c> on
/// <see cref="TurnInput.Metadata"/>. This test is the canonical
/// proof: a real protobuf-net <c>Serializer.Serialize</c> /
/// <c>Serializer.Deserialize&lt;T&gt;</c> roundtrip over a
/// <see cref="MemoryStream"/>, not the in-memory channel the worker
/// uses.
/// </summary>
public sealed class TurnInputProtoRoundtripShould
{
    [Fact(DisplayName = "Given a TurnInput with metadata, when the protobuf-net wire roundtrip runs, then the metadata survives")]
    public void MetadataSurvivesProtobufNetRoundtrip()
    {
        var original = new TurnInput
        {
            Text = "actually do this instead",
            Role = "user",
            Metadata = new Dictionary<string, string>
            {
                ["as"] = "steer",
                ["traceId"] = "01HXRND-ABCD",
            },
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        var bytes = stream.ToArray();
        bytes.ShouldNotBeNull("the wire shape must serialise to non-empty bytes");
        bytes.Length.ShouldBeGreaterThan(0, "the wire shape must produce bytes");

        using var readStream = new MemoryStream(bytes);
        var roundtripped = Serializer.Deserialize<TurnInput>(readStream);

        roundtripped.ShouldNotBeNull();
        roundtripped.Text.ShouldBe(original.Text);
        roundtripped.Role.ShouldBe(original.Role);
        roundtripped.Metadata.ShouldNotBeNull("the metadata must roundtrip — a lost field would corrupt the test harness's steer / follow_up dispatch");
        roundtripped.Metadata.Count.ShouldBe(2);
        roundtripped.Metadata["as"].ShouldBe("steer");
        roundtripped.Metadata["traceId"].ShouldBe("01HXRND-ABCD");
    }

    [Fact(DisplayName = "Given a TurnInput with an empty metadata dictionary, when the protobuf-net wire roundtrip runs, then the empty map survives")]
    public void EmptyMetadataSurvivesProtobufNetRoundtrip()
    {
        var original = new TurnInput
        {
            Text = "the first turn",
            Role = "user",
            Metadata = [],
        };

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, original);
        var bytes = stream.ToArray();

        using var readStream = new MemoryStream(bytes);
        var roundtripped = Serializer.Deserialize<TurnInput>(readStream);

        roundtripped.ShouldNotBeNull();
        roundtripped.Metadata.ShouldNotBeNull();
        roundtripped.Metadata.Count.ShouldBe(0);
    }
}
