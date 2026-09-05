using Comuki.Modules.Intake.Application.Ports.Sources;
using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Application.Sources;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Ids;
using Comuki.Modules.Intake.Domain.Rules;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Shared.Kernel.Ids;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Source connection + admission rule CRUD services over mocked store ports,
/// plus structural validators for create commands.
/// </summary>
public sealed class IntakeSourceServicesShould
{
    private readonly DateTimeOffset now = new(2026, 9, 1, 21, 0, 0, TimeSpan.Zero);
    private readonly IIntakeStore store = Substitute.For<IIntakeStore>();
    private readonly ISecretResolver secrets = Substitute.For<ISecretResolver>();
    private readonly FakeTime clock;

    public IntakeSourceServicesShould()
    {
        clock = new FakeTime(now);
        // Default: every env-var name resolves to a non-empty value so the
        // happy-path tests do not need to stub the resolver. The two
        // missing-secret tests stub a single name to null/empty.
        secrets.Resolve(Arg.Any<string?>()).Returns("resolved-secret");
    }

    [Fact(DisplayName = "Given a valid connection command, when Create runs, then the store receives it and the view has a hook path")]
    public async Task CreateConnectionPersistsAsync()
    {
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        var view = await service.CreateAsync(
            new CreateSourceConnectionCommand(
                ProjectId.New(),
                "github",
                "Main",
                                     /*lang=json,strict*/
                                     "{\"owner\":\"acme\",\"repo\":\"app\"}",
                "HOOK_SECRET"),
            TestContext.Current.CancellationToken);

        view.Provider.ShouldBe("github");
        view.WebhookPath.ShouldStartWith("/api/hooks/github/");
        view.Enabled.ShouldBeTrue();
        await store.Received(1).AddConnectionAsync(Arg.Any<SourceConnection>(), Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "Given an invalid connection command, when validated, then it fails")]
    [InlineData("native", "Main", "{}", "HOOK")]
    [InlineData("github", "", "{}", "HOOK")]
    [InlineData("github", "Main", "not-json", "HOOK")]
    [InlineData("github", "Main", "{}", "bad-secret!")]
    public void RefuseInvalidConnection(string provider, string name, string settings, string secret)
    {
        new CreateSourceConnectionValidator()
            .Validate(new CreateSourceConnectionCommand(ProjectId.New(), provider, name, settings, secret))
            .IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given connections in the store, when List/Get run, then views are mapped")]
    public async Task ListAndGetConnectionAsync()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitLab,
            "GL",
            "{}",
            "SEC",
            "abcdefghijklmnop",
            now);
        store.ListConnectionsAsync(null, Arg.Any<CancellationToken>()).Returns([connection]);
        store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>()).Returns(connection);
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        var listed = await service.ListAsync(null, TestContext.Current.CancellationToken);
        var got = await service.GetAsync(connection.Id, TestContext.Current.CancellationToken);

        listed.ShouldHaveSingleItem().Id.ShouldBe(connection.Id.Value);
        got.Name.ShouldBe("GL");
        got.WebhookPath.ShouldBe("/api/hooks/gitlab/abcdefghijklmnop");
    }

    [Fact(DisplayName = "Given a missing connection, when Get runs, then SourceConnectionNotFoundException is thrown")]
    public async Task GetMissingConnectionThrowsAsync()
    {
        store.FindConnectionAsync(Arg.Any<SourceConnectionId>(), Arg.Any<CancellationToken>()).Returns((SourceConnection?)null);
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        await Should.ThrowAsync<SourceConnectionNotFoundException>(
            () => service.GetAsync(SourceConnectionId.New(), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an existing connection, when Update runs, then the store is updated")]
    public async Task UpdateConnectionPersistsAsync()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.Jira,
            "Old",
            "{}",
            "SEC",
            "abcdefghijklmnop",
            now);
        store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>()).Returns(connection);
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        var view = await service.UpdateAsync(connection.Id, "New", null, null, false, TestContext.Current.CancellationToken);

        view.Name.ShouldBe("New");
        view.Enabled.ShouldBeFalse();
        await store.Received(1).UpdateConnectionAsync(connection, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a valid admission rule command, when Create runs, then watch mode is persisted")]
    public async Task CreateAdmissionRulePersistsAsync()
    {
        var service = new AdmissionRuleService(
            store,
            clock,
            new CreateAdmissionRuleValidator(),
            NullLogger<AdmissionRuleService>.Instance);

        var view = await service.CreateAsync(
            new CreateAdmissionRuleCommand(ProjectId.New(), "watch", /*lang=json,strict*/ "{\"labelsAny\":[\"comuki\"]}"),
            TestContext.Current.CancellationToken);

        view.Mode.ShouldBe("watch");
        view.Enabled.ShouldBeTrue();
        await store.Received(1).AddRuleAsync(
            Arg.Is<AdmissionRule>(static rule => rule.Mode == AdmissionMode.Watch),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an invalid admission mode, when validated, then it fails")]
    public void RefuseInvalidAdmissionMode()
    {
        new CreateAdmissionRuleValidator()
            .Validate(new CreateAdmissionRuleCommand(ProjectId.New(), "auto", "{}"))
            .IsValid.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an existing rule, when Update runs with inbox mode, then the store is updated")]
    public async Task UpdateAdmissionRulePersistsAsync()
    {
        var rule = AdmissionRule.Create(ProjectId.New(), AdmissionMode.Watch, "{}", now);
        store.FindRuleAsync(rule.Id, Arg.Any<CancellationToken>()).Returns(rule);
        var service = new AdmissionRuleService(
            store,
            clock,
            new CreateAdmissionRuleValidator(),
            NullLogger<AdmissionRuleService>.Instance);

        var view = await service.UpdateAsync(rule.Id, "inbox", /*lang=json,strict*/ "{\"projects\":[\"a\"]}", true, TestContext.Current.CancellationToken);

        view.Mode.ShouldBe("inbox");
        await store.Received(1).UpdateRuleAsync(rule, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a missing rule, when Get runs, then AdmissionRuleNotFoundException is thrown")]
    public async Task GetMissingRuleThrowsAsync()
    {
        store.FindRuleAsync(Arg.Any<AdmissionRuleId>(), Arg.Any<CancellationToken>()).Returns((AdmissionRule?)null);
        var service = new AdmissionRuleService(
            store,
            clock,
            new CreateAdmissionRuleValidator(),
            NullLogger<AdmissionRuleService>.Instance);

        await Should.ThrowAsync<AdmissionRuleNotFoundException>(
            () => service.GetAsync(AdmissionRuleId.New(), TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given rules in the store, when List runs, then views are mapped")]
    public async Task ListRulesAsync()
    {
        var rule = AdmissionRule.Create(ProjectId.New(), AdmissionMode.Inbox, "{}", now);
        store.ListRulesAsync(null, Arg.Any<CancellationToken>()).Returns([rule]);
        var service = new AdmissionRuleService(
            store,
            clock,
            new CreateAdmissionRuleValidator(),
            NullLogger<AdmissionRuleService>.Instance);

        var listed = await service.ListAsync(null, TestContext.Current.CancellationToken);

        listed.ShouldHaveSingleItem().Mode.ShouldBe("inbox");
    }

    [Fact(DisplayName = "Given a bad mode string on Update, when Update runs, then ValidationException is thrown")]
    public async Task UpdateRuleRefusesBadModeAsync()
    {
        var rule = AdmissionRule.Create(ProjectId.New(), AdmissionMode.Watch, "{}", now);
        store.FindRuleAsync(rule.Id, Arg.Any<CancellationToken>()).Returns(rule);
        var service = new AdmissionRuleService(
            store,
            clock,
            new CreateAdmissionRuleValidator(),
            NullLogger<AdmissionRuleService>.Instance);

        await Should.ThrowAsync<ValidationException>(
            () => service.UpdateAsync(rule.Id, "auto", null, null, TestContext.Current.CancellationToken));
    }

    [Fact(DisplayName = "Given an unset secret env var, when Create runs, then SecretEnvRefUnsetException is thrown and the store stays untouched")]
    public async Task CreateConnectionRefusesUnsetSecretEnvRefAsync()
    {
        secrets.Resolve("MISSING_TOKEN").Returns((string?)null);
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        var exception = await Should.ThrowAsync<SecretEnvRefUnsetException>(
            () => service.CreateAsync(
                new CreateSourceConnectionCommand(
                    ProjectId.New(),
                    "github",
                    "Main",
                    /*lang=json,strict*/ "{}",
                    "MISSING_TOKEN"),
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("MISSING_TOKEN");
        await store.DidNotReceive().AddConnectionAsync(Arg.Any<SourceConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a resolved-but-empty secret, when Create runs, then SecretEnvRefUnsetException is thrown")]
    public async Task CreateConnectionRefusesEmptyResolvedSecretAsync()
    {
        secrets.Resolve("EMPTY_TOKEN").Returns(string.Empty);
        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        await Should.ThrowAsync<SecretEnvRefUnsetException>(
            () => service.CreateAsync(
                new CreateSourceConnectionCommand(
                    ProjectId.New(),
                    "github",
                    "Main",
                    /*lang=json,strict*/ "{}",
                    "EMPTY_TOKEN"),
                TestContext.Current.CancellationToken));

        await store.DidNotReceive().AddConnectionAsync(Arg.Any<SourceConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given a new unset secret env var on Update, when Update runs, then SecretEnvRefUnsetException is thrown")]
    public async Task UpdateConnectionRefusesUnsetSecretEnvRefAsync()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "Old",
            "{}",
            "OLD_REF",
            "abcdefghijklmnop",
            now);
        store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>()).Returns(connection);
        secrets.Resolve("NEW_MISSING").Returns((string?)null);

        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        await Should.ThrowAsync<SecretEnvRefUnsetException>(
            () => service.UpdateAsync(connection.Id, null, null, "NEW_MISSING", null, TestContext.Current.CancellationToken));

        await store.DidNotReceive().UpdateConnectionAsync(Arg.Any<SourceConnection>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "Given an unset stored secret, when Update runs without touching the secret, then no resolver call is made")]
    public async Task UpdateConnectionSkipsResolverWhenSecretUnchangedAsync()
    {
        var connection = SourceConnection.Create(
            ProjectId.New(),
            TicketProvider.GitHub,
            "Old",
            "{}",
            "OLD_REF",
            "abcdefghijklmnop",
            now);
        store.FindConnectionAsync(connection.Id, Arg.Any<CancellationToken>()).Returns(connection);
        secrets.Resolve("OLD_REF").Returns((string?)null);

        var service = new SourceConnectionService(
            store,
            clock,
            new CreateSourceConnectionValidator(),
            secrets,
            NullLogger<SourceConnectionService>.Instance);

        // A null secretEnvRef means "keep the stored value"; the resolver is
        // not consulted, so an already-broken stored connection does not
        // surface a 400 on every subsequent edit.
        var view = await service.UpdateAsync(connection.Id, "Renamed", null, null, null, TestContext.Current.CancellationToken);

        view.Name.ShouldBe("Renamed");
        secrets.DidNotReceive().Resolve(Arg.Any<string?>());
        await store.Received(1).UpdateConnectionAsync(connection, Arg.Any<CancellationToken>());
    }

    private sealed class FakeTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
