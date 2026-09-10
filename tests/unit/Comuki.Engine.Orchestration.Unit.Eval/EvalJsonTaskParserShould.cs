using Comuki.Engine.Orchestration.Unit.Eval.Eval;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Orchestration.Unit.Eval;

/// <summary>
/// Unit tests for <see cref="EvalJsonTaskParser"/>: round-trip happy
/// path, schema-version gate, and the per-field failure surfaces
/// (origin, missing field, malformed JSON, unknown enum).
/// </summary>
public sealed class EvalJsonTaskParserShould
{
    [Fact(DisplayName = "Given a valid run-kind JSON, when parsed, then it carries the right id + kind + operations + expected")]
    public void ParseHappyRunTask()
    {
        var body = "{\n" +
                   "  \"schemaVersion\": 1,\n" +
                   "  \"id\": \"01-run-succeed\",\n" +
                   "  \"name\": \"Queued -> Running -> Succeeded\",\n" +
                   "  \"kind\": \"Run\",\n" +
                   "  \"operations\": [\n" +
                   "    { \"action\": \"Create\" },\n" +
                   "    { \"action\": \"Transition\", \"status\": \"Running\" },\n" +
                                                                                  /*lang=json,strict*/
                                                                                  /*lang=json,strict*/
                                                                                  /*lang=json,strict*/
                                                                                  "    { \"action\": \"Transition\", \"status\": \"Succeeded\" }\n" +
                   "  ],\n" +
                   "  \"expected\": {\n" +
                   "    \"finalStatus\": \"Succeeded\",\n" +
                   "    \"transitionLog\": [\"Queued\", \"Running\", \"Succeeded\"]\n" +
                   "  }\n" +
                   "}";

        var task = EvalJsonTaskParser.Parse(body);

        task.Id.ShouldBe("01-run-succeed");
        task.Kind.ShouldBe(EvalTaskKind.Run);
        task.Operations.Count.ShouldBe(3);
        task.Operations[0].Action.ShouldBe(EvalAction.Create);
        task.Operations[1].Status.ShouldBe("Running");
        task.Expected.FinalStatus.ShouldBe("Succeeded");
        task.Expected.TransitionLog.ShouldBe(["Queued", "Running", "Succeeded"]);
    }

    [Fact(DisplayName = "Given an expectsFailure JSON, when parsed, then the runner expects an exception containing the message")]
    public void ParseFailureExpectation()
    {
        var body = "{\n" +
                   "  \"schemaVersion\": 1,\n" +
                   "  \"id\": \"negative\",\n" +
                   "  \"name\": \"illegal transition\",\n" +
                   "  \"kind\": \"Run\",\n" +
                   "  \"operations\": [\n" +
                   "    { \"action\": \"Create\" },\n" +
                   "    { \"action\": \"Transition\", \"status\": \"Running\" },\n" +
                   "    { \"action\": \"Transition\", \"status\": \"Succeeded\" },\n" +
                                                                                  /*lang=json,strict*/
                                                                                  /*lang=json,strict*/
                                                                                  /*lang=json,strict*/
                                                                                  "    { \"action\": \"TransitionExpectFailure\", \"status\": \"Running\" }\n" +
                   "  ],\n" +
                   "  \"expected\": {\n" +
                   "    \"expectsFailure\": true,\n" +
                   "    \"expectedFailureMessage\": \"Succeeded\"\n" +
                   "  }\n" +
                   "}";

        var task = EvalJsonTaskParser.Parse(body);

        task.Expected.ExpectsFailure.ShouldBeTrue();
        task.Expected.ExpectedFailureMessage.ShouldBe("Succeeded");
        task.Expected.FinalStatus.ShouldBe(string.Empty);
        task.Expected.TransitionLog.ShouldBeEmpty();
    }

    [Fact(DisplayName = "Given a wrong schemaVersion, when parsed, then EvalParseException names the version")]
    public void RejectWrongSchemaVersion()
    {
        var body = /*lang=json,strict*/ "{ \"schemaVersion\": 999, \"id\": \"x\", \"name\": \"x\", \"kind\": \"Run\", \"operations\": [{\"action\":\"Create\"}], \"expected\": {\"finalStatus\":\"Queued\"} }";

        var exception = Should.Throw<EvalParseException>(() => EvalJsonTaskParser.Parse(body));
        exception.Message.ShouldContain("schema version mismatch");
        exception.Message.ShouldContain("999");
    }

    [Fact(DisplayName = "Given an empty operations list, when parsed, then EvalParseException names the offending task")]
    public void RejectEmptyOperations()
    {
        var body = /*lang=json,strict*/ "{ \"schemaVersion\": 1, \"id\": \"empty\", \"name\": \"empty\", \"kind\": \"Run\", \"operations\": [], \"expected\": {\"finalStatus\":\"Queued\"} }";

        var exception = Should.Throw<EvalParseException>(() => EvalJsonTaskParser.Parse(body));
        exception.Message.ShouldContain("no operations");
        exception.Message.ShouldContain("empty");
    }

    [Fact(DisplayName = "Given a malformed JSON body, when parsed, then EvalParseException surfaces the parser message")]
    public void RejectMalformedJson()
    {
        var body = "{ this is not json";

        var exception = Should.Throw<EvalParseException>(() => EvalJsonTaskParser.Parse(body));
        exception.Origin.ShouldBe("<inline>");
        exception.Message.ShouldContain("malformed JSON");
    }

    [Fact(DisplayName = "Given a missing expected outcome, when parsed, then EvalParseException names the task")]
    public void RejectEmptyExpected()
    {
        var body = /*lang=json,strict*/ "{ \"schemaVersion\": 1, \"id\": \"x\", \"name\": \"x\", \"kind\": \"Run\", \"operations\": [{\"action\":\"Create\"}], \"expected\": {} }";

        var exception = Should.Throw<EvalParseException>(() => EvalJsonTaskParser.Parse(body));
        exception.Message.ShouldContain("expected outcome is empty");
    }

    [Fact(DisplayName = "Given an unknown action, when parsed, then EvalParseException names the action")]
    public void RejectUnknownAction()
    {
        var body = /*lang=json,strict*/ "{ \"schemaVersion\": 1, \"id\": \"x\", \"name\": \"x\", \"kind\": \"Run\", \"operations\": [{\"action\":\"Detonate\"}], \"expected\": {\"finalStatus\":\"Queued\"} }";

        var exception = Should.Throw<EvalParseException>(() => EvalJsonTaskParser.Parse(body));
        exception.Message.ShouldContain("Detonate");
    }
}
