using System.Text.Json;
using Comuki.Engine.Orchestration.Domain.Journal;
using Comuki.Engine.Orchestration.Infrastructure.Queue;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.StatusMachine;

/// <summary>
/// WS8 (issue #87): event-contract compatibility gate for
/// <c>orchestration.run.*.v1</c> outbox payloads. Per architecture.md's
/// Integration Event Contract (design.md decision #7), evolution within a
/// major version must stay additive-only — new optional fields are fine,
/// removing, renaming, or retyping a published field is a breaking change
/// that must ship as a new major (<c>.v2</c>). Two things are asserted:
/// (1) the CURRENT <see cref="RunEventTypes.RunTerminatedV1"/> payload
/// still contains every field recorded in the committed golden schema
/// snapshot under <c>EventContracts/</c> — this is the actual regression
/// gate: a future edit to <see cref="RunProgression.RunTerminatedPayload"/>
/// that drops or retypes a field fails this test; (2) the comparer itself
/// correctly flags a removed/retyped/renamed field when given a synthetic
/// breaking candidate — the negative case, proving the gate would actually
/// catch a real regression rather than only passing on the happy path.
/// </summary>
public sealed class OrchestrationRunEventContractGateShould
{
    private static readonly DateTimeOffset occurredAt = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given the committed v1 golden schema, when compared to the live RunTerminatedV1 payload, then every golden field is still present with the same JSON kind")]
    public void KeepEveryGoldenFieldOnTheLivePayload()
    {
        var golden = EventContractSchema.LoadGolden(EventContractSchema.GoldenSchemaPath(RunEventTypes.RunTerminatedV1));

        var livePayload = RunProgression.RunTerminatedPayload(RunId.New(), ProjectId.New(), "Succeeded", occurredAt);
        var live = EventContractSchema.ExtractFieldKinds(livePayload);

        var violations = EventContractSchema.FindBreakingChanges(golden.Fields, live);

        violations.ShouldBeEmpty(
            $"{RunEventTypes.RunTerminatedV1} must stay additive-only vs. the committed golden schema; " +
            $"violations: {string.Join(" | ", violations)}");
    }

    [Fact(DisplayName = "Given the golden schema name, when read, then it matches the RunTerminatedV1 contract constant")]
    public void GoldenSchemaNamesTheV1Contract()
    {
        var golden = EventContractSchema.LoadGolden(EventContractSchema.GoldenSchemaPath(RunEventTypes.RunTerminatedV1));

        golden.Type.ShouldBe(RunEventTypes.RunTerminatedV1);
    }

    [Fact(DisplayName = "Given a candidate payload with a field removed, when compared to golden, then the gate reports a breaking change")]
    public void FlagARemovedField()
    {
        var golden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runId"] = "string",
            ["projectId"] = "string",
            ["status"] = "string",
            ["occurredAt"] = "string",
        };

        // Simulates a future edit that drops the `status` field.
        var candidatePayload = /*lang=json,strict*/ """{"runId":"11111111-1111-1111-1111-111111111111","projectId":"22222222-2222-2222-2222-222222222222","occurredAt":"2026-09-25T10:00:00+00:00"}""";
        var candidate = EventContractSchema.ExtractFieldKinds(candidatePayload);

        var violations = EventContractSchema.FindBreakingChanges(golden, candidate);

        violations.ShouldNotBeEmpty();
        violations.ShouldContain(static violation => violation.Contains("status", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a candidate payload with a field retyped, when compared to golden, then the gate reports a breaking change")]
    public void FlagARetypedField()
    {
        var golden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runId"] = "string",
            ["status"] = "string",
        };

        // Simulates a future edit that turns `status` from a string into a number.
        var candidatePayload = /*lang=json,strict*/ """{"runId":"11111111-1111-1111-1111-111111111111","status":1}""";
        var candidate = EventContractSchema.ExtractFieldKinds(candidatePayload);

        var violations = EventContractSchema.FindBreakingChanges(golden, candidate);

        violations.ShouldNotBeEmpty();
        violations.ShouldContain(static violation => violation.Contains("status", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a candidate payload with a field renamed, when compared to golden, then the gate reports a breaking change (the old name is gone)")]
    public void FlagARenamedField()
    {
        var golden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runId"] = "string",
            ["status"] = "string",
        };

        // Simulates a future edit that renames `status` to `runStatus`.
        var candidatePayload = /*lang=json,strict*/ """{"runId":"11111111-1111-1111-1111-111111111111","runStatus":"Succeeded"}""";
        var candidate = EventContractSchema.ExtractFieldKinds(candidatePayload);

        var violations = EventContractSchema.FindBreakingChanges(golden, candidate);

        violations.ShouldNotBeEmpty();
        violations.ShouldContain(static violation => violation.Contains("status", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a candidate payload with a new optional field added, when compared to golden, then the gate reports no breaking change")]
    public void AllowAnAddedOptionalField()
    {
        var golden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["runId"] = "string",
            ["status"] = "string",
        };

        // Simulates a future additive-only edit that adds a new optional field.
        var candidatePayload = /*lang=json,strict*/ """{"runId":"11111111-1111-1111-1111-111111111111","status":"Succeeded","attempt":2}""";
        var candidate = EventContractSchema.ExtractFieldKinds(candidatePayload);

        var violations = EventContractSchema.FindBreakingChanges(golden, candidate);

        violations.ShouldBeEmpty();
    }
}

/// <summary>
/// Golden schema snapshot for one event-contract type: the frozen field
/// name -> JSON-kind map committed under <c>EventContracts/*.schema.json</c>,
/// loaded and diffed by <see cref="EventContractSchema"/>.
/// </summary>
file sealed record EventContractGoldenSchema(string Type, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Pure, file-scoped comparer for the event-contract compatibility gate:
/// loads a committed golden schema snapshot, extracts the field/JSON-kind
/// map of a live payload, and reports any field the live payload dropped
/// or retyped relative to golden. Adding a field is never a violation
/// (additive-only evolution); removing, renaming (which looks like a
/// removal from golden's point of view), or changing a field's JSON kind
/// always is. File-scoped so the public test class above stays the only
/// public type in this file (project rule: one public type per file, no
/// private methods — pull logic into a file-scoped helper instead).
/// </summary>
file static class EventContractSchema
{
    public static string GoldenSchemaPath(string contractType)
    {
        return Path.Combine(AppContext.BaseDirectory, "EventContracts", $"{contractType}.schema.json");
    }

    public static EventContractGoldenSchema LoadGolden(string path)
    {
        File.Exists(path).ShouldBeTrue($"golden event-contract schema should exist at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var type = document.RootElement.GetProperty("type").GetString()
            ?? throw new InvalidOperationException($"golden schema at {path} has no 'type'");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.GetProperty("fields").EnumerateObject())
        {
            fields[field.Name] = field.Value.GetString()
                ?? throw new InvalidOperationException($"golden schema at {path} field '{field.Name}' has no kind string");
        }

        return new EventContractGoldenSchema(type, fields);
    }

    public static IReadOnlyDictionary<string, string> ExtractFieldKinds(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            fields[property.Name] = DescribeKind(property.Value);
        }

        return fields;
    }

    public static IReadOnlyList<string> FindBreakingChanges(
        IReadOnlyDictionary<string, string> golden,
        IReadOnlyDictionary<string, string> candidate)
    {
        var violations = new List<string>();

        foreach (var (field, kind) in golden)
        {
            if (!candidate.TryGetValue(field, out var candidateKind))
            {
                violations.Add($"field '{field}' is missing from the candidate payload (removed or renamed)");
                continue;
            }

            if (!string.Equals(kind, candidateKind, StringComparison.Ordinal))
            {
                violations.Add($"field '{field}' changed JSON kind from '{kind}' to '{candidateKind}'");
            }
        }

        return violations;
    }

    public static string DescribeKind(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.Null => "null",
            _ => "unknown",
        };
    }
}
