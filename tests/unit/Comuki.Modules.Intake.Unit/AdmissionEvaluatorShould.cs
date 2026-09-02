using Comuki.Modules.Intake.Application.Admission;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// The admission evaluator truth table: filter semantics (empty =
/// match-everything, any-label intersection, project list) and the
/// first-matching-rule-wins ordering.
/// </summary>
public sealed class AdmissionEvaluatorShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given an empty filter, when evaluated, then every ticket matches")]
    public void MatchEverythingOnEmptyFilter()
    {
        var ticket = Ticket(labels: [], projectKey: null);

        AdmissionEvaluator.Matches(AdmissionFilter.Any, ticket).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a labelsAny filter, when the ticket labels are evaluated, then the any-intersection rule decides")]
    [InlineData("bug", true)]
    [InlineData("BUG", true)]
    [InlineData("feature", false)]
    public void EvaluateLabelsAny(string ticketLabel, bool expected)
    {
        var filter = Parse(/*lang=json,strict*/ """{"labelsAny": ["bug", "comuki"]}""");
        var ticket = Ticket(labels: [ticketLabel], projectKey: null);

        AdmissionEvaluator.Matches(filter, ticket).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a labelsAny filter and a labelless ticket, then it does not match")]
    public void RefuseLabellessTicket()
    {
        var filter = Parse(/*lang=json,strict*/ """{"labelsAny": ["bug"]}""");

        AdmissionEvaluator.Matches(filter, Ticket(labels: [], projectKey: null)).ShouldBeFalse();
    }

    [Theory(DisplayName = "Given a projects filter, when the ticket project key is evaluated, then membership decides")]
    [InlineData("dot-stbl/comuki", true)]
    [InlineData("DOT-STBL/other", false)]
    public void EvaluateProjects(string ticketProjectKey, bool expected)
    {
        var filter = Parse(/*lang=json,strict*/ """{"projects": ["dot-stbl/comuki"]}""");
        var ticket = Ticket(labels: [], projectKey: ticketProjectKey);

        AdmissionEvaluator.Matches(filter, ticket).ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a projects filter and a ticket without a project key, then it does not match")]
    public void RefuseKeylessTicket()
    {
        var filter = Parse(/*lang=json,strict*/ """{"projects": ["COMUKI"]}""");

        AdmissionEvaluator.Matches(filter, Ticket(labels: [], projectKey: null)).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given both lists, when only one axis matches, then the conjunction refuses")]
    public void ConjoinBothAxes()
    {
        var filter = Parse(/*lang=json,strict*/ """{"labelsAny": ["bug"], "projects": ["dot-stbl/comuki"]}""");
        var rightLabelWrongProject = Ticket(labels: ["bug"], projectKey: "other/other");
        var wrongLabelRightProject = Ticket(labels: ["feature"], projectKey: "dot-stbl/comuki");

        AdmissionEvaluator.Matches(filter, rightLabelWrongProject).ShouldBeFalse();
        AdmissionEvaluator.Matches(filter, wrongLabelRightProject).ShouldBeFalse();
        AdmissionEvaluator.Matches(filter, Ticket(labels: ["bug"], projectKey: "dot-stbl/comuki")).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given several rules, when the first matches, then its mode wins")]
    public void FirstMatchingRuleDecides()
    {
        var ticket = Ticket(labels: ["comuki"], projectKey: null);
        var rules = new[]
        {
            Rule(AdmissionMode.Inbox, /*lang=json,strict*/ """{"labelsAny": ["bug"]}"""),
            Rule(AdmissionMode.Watch, /*lang=json,strict*/ """{"labelsAny": ["comuki"]}"""),
            Rule(AdmissionMode.Inbox, "{}"),
        };

        AdmissionEvaluator.Evaluate(rules, ticket).ShouldBe(AdmissionMode.Watch);
    }

    [Fact(DisplayName = "Given no matching rule, when evaluated, then the answer is filtered out")]
    public void FilterOutWhenNoRuleMatches()
    {
        var ticket = Ticket(labels: [], projectKey: null);

        AdmissionEvaluator.Evaluate([], ticket).ShouldBeNull();
        AdmissionEvaluator.Evaluate([Rule(AdmissionMode.Watch, /*lang=json,strict*/ """{"labelsAny": ["bug"]}""")], ticket).ShouldBeNull();
    }

    private static AdmissionFilter Parse(string json)
    {
        return AdmissionFilter.Parse(json);
    }

    private static AdmissionRule Rule(AdmissionMode mode, string filterJson)
    {
        return AdmissionRule.Create(projectId, mode, filterJson, now);
    }

    private static IncomingTicket Ticket(string[] labels, string? projectKey)
    {
        return IncomingTicket.Create(
            projectId,
            TicketProvider.GitHub,
            "dot-stbl/comuki#1",
            "title",
            string.Empty,
            "author",
            "https://example.com",
            projectKey,
            labels,
            now);
    }
}
