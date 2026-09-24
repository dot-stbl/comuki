using Comuki.Modules.Chat.Application.Graph.Catalog;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Chat.Unit;

/// <summary>
/// Intent heuristic cases for the router's task detection: imperative verb
/// within the leading window, question openers keep the message in chat
/// mode, and verb-forms with punctuation still count.
/// </summary>
public sealed class ChatIntentShould
{
    [Theory(DisplayName = "Given a task-shaped message, when LooksLikeTask is called, then it is true")]
    [InlineData("Implement this exactly: append the line")]
    [InlineData("write a readme for the project")]
    [InlineData("Fix the flaky heartbeat test")]
    [InlineData("In the repository, edit hello.txt and append one line")]
    [InlineData("In the repo: update the token")]
    [InlineData("setup the ingress")]
    public void ReturnTrueForTaskShapedMessages(string message)
    {
        ChatIntent.LooksLikeTask(message).ShouldBeTrue();
    }

    [Theory(DisplayName = "Given a question or conversation, when LooksLikeTask is called, then it is false")]
    [InlineData("what should we build next?")]
    [InlineData("how does the claim loop work?")]
    [InlineData("could you explain the plan gate?")]
    [InlineData("is the worker running?")]
    [InlineData("thanks, that helps")]
    [InlineData("")]
    public void ReturnFalseForConversation(string message)
    {
        ChatIntent.LooksLikeTask(message).ShouldBeFalse();
    }
}
