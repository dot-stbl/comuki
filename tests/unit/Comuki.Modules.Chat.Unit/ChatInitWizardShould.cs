using Comuki.Modules.Chat.Domain.Messages;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// /init onboarding wizard skeleton (issue #5 slice B): steps repo →
/// compute → models → knowledge as prompts, answers stored on the thread,
/// final summary JSON. Provisioning is a documented follow-up.
/// </summary>
public sealed class ChatInitWizardShould
{
    [Fact(DisplayName = "Given /init, when posted, then the wizard asks the first step question")]
    public async Task AskFirstStepAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        var result = await harness.Turns.PostAsync(session, "/init", TestContext.Current.CancellationToken);

        var reply = result.NewMessages.ShouldHaveSingleItem();
        reply.Role.ShouldBe(ChatMessageRole.Assistant);
        reply.Content.ShouldContain("Step 1/4");
        reply.Content.ShouldContain("repository");
    }

    [Fact(DisplayName = "Given the wizard is active, when all four answers are posted, then the final summary JSON carries them")]
    public async Task CollectAnswersAndSummarizeAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await harness.Turns.PostAsync(session, "/init", TestContext.Current.CancellationToken);

        string[] answers = ["https://git.example/acme", "docker", "openai", "skip"];
        foreach (var answer in answers)
        {
            await harness.Turns.PostAsync(
                await harness.SessionAsync(sessionId), answer, TestContext.Current.CancellationToken);
        }

        var final = harness.Store.Messages.Single(static message =>
            message.Role == ChatMessageRole.Assistant && message.Content.Contains("Onboarding answers collected"));

        final.Content.ShouldContain("\"repo\":\"https://git.example/acme\"");
        final.Content.ShouldContain("\"compute\":\"docker\"");
        final.Content.ShouldContain("\"models\":\"openai\"");
        final.Content.ShouldContain("\"knowledge\":\"skip\"");
        final.Content.ShouldContain("\"wizard\":\"init\"");
    }

    [Fact(DisplayName = "Given a finished wizard, when a plain message is posted, then the graph routes normally again")]
    public async Task WizardFinishesAndRoutesNormallyAsync()
    {
        await using var harness = ChatHarness.Create();
        var sessionId = await harness.NewSessionAsync();
        var session = await harness.SessionAsync(sessionId);

        await harness.Turns.PostAsync(session, "/init", TestContext.Current.CancellationToken);
        foreach (var answer in (string[])["repo-url", "k3s", "provider", "skip"])
        {
            await harness.Turns.PostAsync(
                await harness.SessionAsync(sessionId), answer, TestContext.Current.CancellationToken);
        }

        var result = await harness.Turns.PostAsync(
            await harness.SessionAsync(sessionId), "hello there", TestContext.Current.CancellationToken);

        var reply = result.NewMessages.Single(static message => message.Role == ChatMessageRole.Assistant);
        reply.Content.ShouldBe("brain says: hello there");
    }
}
