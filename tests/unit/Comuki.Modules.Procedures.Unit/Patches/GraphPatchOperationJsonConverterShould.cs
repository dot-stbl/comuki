using System.Text.Json;
using Comuki.Modules.Procedures.Application.Patches.Drafting;
using Comuki.Modules.Procedures.Domain.Definitions.Elements;
using Comuki.Modules.Procedures.Domain.Patches.Model;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Procedures.Unit.Patches;

/// <summary>
/// Unit tests for the polymorphic
/// <see cref="GraphPatchOperationJsonConverter"/> — the closed
/// <see cref="GraphPatchOperation"/> hierarchy rides a single
/// converter (per <c>json-and-ndjson.md</c> §5: switch expression on
/// the discriminator, no god-if). Phase A.2 introduces the wire-side
/// <c>operations</c> field on the propose-patch body; the converter
/// is what the host's MVC controller uses to deserialize each element.
/// </summary>
public sealed class GraphPatchOperationJsonConverterShould
{
    private static readonly JsonSerializerOptions webOptions = new(JsonSerializerDefaults.Web);

    [Fact(DisplayName = "Given an add-node operation, when read, then it produces a typed AddNode record")]
    public void DeserializeAddNode()
    {
        var json = /*lang=json,strict*/ """{"kind":"add-node","node":{"id":"intake-2","kindKey":"intake","parameters":{"k":"v"}}}""";

        var operation = ReadOne(json);

        var add = operation.ShouldBeOfType<GraphPatchOperation.AddNode>();
        add.Node.Id.ShouldBe("intake-2");
        add.Node.KindKey.ShouldBe("intake");
        add.Node.Parameters["k"].ShouldBe("v");
    }

    [Fact(DisplayName = "Given a remove-node operation, when read, then it produces a typed RemoveNode record")]
    public void DeserializeRemoveNode()
    {
        var json = /*lang=json,strict*/ """{"kind":"remove-node","nodeId":"intake-2"}""";

        var operation = ReadOne(json);

        var remove = operation.ShouldBeOfType<GraphPatchOperation.RemoveNode>();
        remove.NodeId.ShouldBe("intake-2");
    }

    [Fact(DisplayName = "Given a rewire-edge operation, when read, then it produces a typed RewireEdge record")]
    public void DeserializeRewireEdge()
    {
        var json = /*lang=json,strict*/ """
            {"kind":"rewire-edge","before":{"fromNodeId":"a","fromPort":"default","toNodeId":"b"},
                          "after":{"fromNodeId":"a","fromPort":"default","toNodeId":"c"}}
            """;

        var operation = ReadOne(json);

        var rewire = operation.ShouldBeOfType<GraphPatchOperation.RewireEdge>();
        rewire.Before.ToNodeId.ShouldBe("b");
        rewire.After.ToNodeId.ShouldBe("c");
    }

    [Fact(DisplayName = "Given a re-parameterize-node operation, when read, then it produces a typed ReParameterizeNode record")]
    public void DeserializeReParameterizeNode()
    {
        var json = /*lang=json,strict*/ """
            {"kind":"re-parameterize-node","nodeId":"intake",
                          "newParameters":{"k1":"v1","k2":"v2"}}
            """;

        var operation = ReadOne(json);

        var reparam = operation.ShouldBeOfType<GraphPatchOperation.ReParameterizeNode>();
        reparam.NodeId.ShouldBe("intake");
        reparam.NewParameters["k1"].ShouldBe("v1");
        reparam.NewParameters["k2"].ShouldBe("v2");
    }

    [Fact(DisplayName = "Given an unknown kind, when read, then it throws a typed JsonException naming the unknown value")]
    public void RefuseUnknownKind()
    {
        var json = /*lang=json,strict*/ """{"kind":"destroy-everything","nodeId":"x"}""";

        var exception = Should.Throw<JsonException>(() => ReadOne(json));
        exception.Message.ShouldContain("destroy-everything");
        exception.Message.ShouldContain("add-node");
    }

    [Fact(DisplayName = "Given a missing kind discriminator, when read, then it throws a typed JsonException naming the missing property")]
    public void RefuseMissingKind()
    {
        var json = /*lang=json,strict*/ """{"nodeId":"x"}""";

        var exception = Should.Throw<JsonException>(() => ReadOne(json));
        exception.Message.ShouldContain("kind");
    }

    [Fact(DisplayName = "Given an add-node missing the node property, when read, then it throws a typed JsonException naming the missing property")]
    public void RefuseMissingNodeBody()
    {
        var json = /*lang=json,strict*/ """{"kind":"add-node"}""";

        var exception = Should.Throw<JsonException>(() => ReadOne(json));
        exception.Message.ShouldContain("node");
    }

    [Fact(DisplayName = "Given an AddNode operation, when written, then it round-trips with the same kind + node body")]
    public void RoundTripAddNode()
    {
        var original = new GraphPatchOperation.AddNode(
            new ProcedureNode("intake-2", "intake", new Dictionary<string, string> { ["k"] = "v" }));

        var json = WriteOne(original);
        var parsed = ReadOne(json);

        var add = parsed.ShouldBeOfType<GraphPatchOperation.AddNode>();
        add.Node.Id.ShouldBe("intake-2");
        add.Node.KindKey.ShouldBe("intake");
        add.Node.Parameters["k"].ShouldBe("v");
    }

    [Fact(DisplayName = "Given a RemoveNode operation, when written, then it round-trips with the same kind + nodeId")]
    public void RoundTripRemoveNode()
    {
        var original = new GraphPatchOperation.RemoveNode("intake-2");

        var json = WriteOne(original);
        var parsed = ReadOne(json);

        var remove = parsed.ShouldBeOfType<GraphPatchOperation.RemoveNode>();
        remove.NodeId.ShouldBe("intake-2");
    }

    private static GraphPatchOperation ReadOne(string json)
    {
        var converter = new GraphPatchOperationJsonConverter();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(converter);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        // Deserialize via STJ so the reader lands on the operation's
        // StartObject the way the host's MVC controller does — direct
        // converter.Read calls need a positioned reader, which STJ's
        // root-level deserialization handles by calling Read() once.
        return JsonSerializer.Deserialize<GraphPatchOperation>(bytes, options)
            ?? throw new InvalidOperationException("Converter returned null.");
    }

    private static string WriteOne(GraphPatchOperation value)
    {
        var converter = new GraphPatchOperationJsonConverter();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            converter.Write(writer, value, webOptions);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
