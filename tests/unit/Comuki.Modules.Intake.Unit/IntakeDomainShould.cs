using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Domain create/update semantics for connections and admission rules.</summary>
public sealed class IntakeDomainShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Given connection fields, when Create is called, then name/secret are trimmed and enabled")]
    public void CreateConnection()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "  Main repo  ",
                                 /*lang=json,strict*/
                                 "{\"owner\":\"acme\"}",
            "  HOOK_SECRET  ",
            "abcdefghijklmnop",
            now);

        connection.Name.ShouldBe("Main repo");
        connection.SecretEnvRef.ShouldBe("HOOK_SECRET");
        connection.WebhookKey.ShouldBe("abcdefghijklmnop");
        connection.Enabled.ShouldBeTrue();
        connection.Id.Value.Version.ShouldBe(7);
        connection.Id.ToString().ShouldBe(connection.Id.Value.ToString());
    }

    [Fact(DisplayName = "Given a connection, when Update patches fields, then only supplied ones change")]
    public void PatchConnection()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.Jira,
            "Old",
            "{}",
            "OLD",
            "keykeykeykeykey1",
            now);
        var later = now.AddHours(1);

        connection.Update(" New ", /*lang=json,strict*/ "{\"x\":1}", " NEW ", false, later);

        connection.Name.ShouldBe("New");
        connection.SettingsJson.ShouldBe(/*lang=json,strict*/ "{\"x\":1}");
        connection.SecretEnvRef.ShouldBe("NEW");
        connection.Enabled.ShouldBeFalse();
        connection.UpdatedAt.ShouldBe(later);
    }

    [Fact(DisplayName = "Given an admission rule, when Create then Update, then mode/filter/enabled mutate")]
    public void CreateAndUpdateRule()
    {
        var rule = AdmissionRule.Create(ProjectId.New(), AdmissionMode.Watch, "{}", now);
        rule.Enabled.ShouldBeTrue();
        rule.Id.Value.Version.ShouldBe(7);

        var later = now.AddMinutes(3);
        rule.Update(AdmissionMode.Inbox, /*lang=json,strict*/ "{\"labelsAny\":[\"bug\"]}", false, later);

        rule.Mode.ShouldBe(AdmissionMode.Inbox);
        rule.FilterJson.ShouldBe(/*lang=json,strict*/ "{\"labelsAny\":[\"bug\"]}");
        rule.Enabled.ShouldBeFalse();
        rule.UpdatedAt.ShouldBe(later);
        rule.Id.ToString().ShouldBe(rule.Id.Value.ToString());
    }

    [Fact(DisplayName = "Given an IncomingTicket Create, when built, then status is Pending and ids mint")]
    public void CreatePendingTicket()
    {
        var ticket = IncomingTicket.Create(
            ProjectId.New(),
            TicketProvider.Native,
            "native-1",
            "Title",
            "Body",
            "ada",
            string.Empty,
            null,
            [],
            now);

        ticket.Status.ShouldBe(IntakeTicketStatus.Pending);
        ticket.RunId.ShouldBeNull();
        ticket.Id.Value.Version.ShouldBe(7);
        ticket.Id.ToString().ShouldBe(ticket.Id.Value.ToString());
    }
}
