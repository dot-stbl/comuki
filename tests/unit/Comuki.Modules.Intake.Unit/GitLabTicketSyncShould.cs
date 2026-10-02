using Comuki.Modules.Intake.Application.Ports.Tickets;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.GitLab;
using Comuki.Shared.Contracts.Runs;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// GitLab sync-back routes by kind: issues get the issues-notes + close
/// path; merge-requests get a single MR-notes call and never close. The
/// malformed-id path throws before any HTTP call.
/// </summary>
public sealed class GitLabTicketSyncShould
{
    [Fact(DisplayName = "Given a Succeeded transition on an issue, when TransitionAsync is called, then the issue note is posted and the issue is closed")]
    public async Task SucceededIssuePostsNoteAndClosesAsync()
    {
        var harness = GitLabTicketSyncHarness.Create();
        var transition = GitLabTicketSyncHarness.Transition("acme/app#7", RunStatuses.Succeeded, InboundTicketKind.Issue);

        await harness.Sync.TransitionAsync(harness.Connection, transition, TestContext.Current.CancellationToken);

        harness.Handler.Requests.Count.ShouldBe(2);
        harness.Handler.Requests[0].Message.Method.ShouldBe(HttpMethod.Post);
        harness.Handler.Requests[0].Message.RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/42/issues/7/notes");
        harness.Handler.Requests[1].Message.Method.ShouldBe(HttpMethod.Put);
        harness.Handler.Requests[1].Message.RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/42/issues/7");
    }

    [Fact(DisplayName = "Given a Succeeded transition on a pull-request, when TransitionAsync is called, then only the MR note is posted and no issue paths are touched")]
    public async Task SucceededMergeRequestPostsOnlyMrNoteAsync()
    {
        var harness = GitLabTicketSyncHarness.Create();
        var transition = GitLabTicketSyncHarness.Transition("acme/app#7", RunStatuses.Succeeded, InboundTicketKind.PullRequest);

        await harness.Sync.TransitionAsync(harness.Connection, transition, TestContext.Current.CancellationToken);

        harness.Handler.Requests.Count.ShouldBe(1);
        harness.Handler.Requests[0].Message.Method.ShouldBe(HttpMethod.Post);
        harness.Handler.Requests[0].Message.RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/42/merge_requests/7/notes");
        harness.Handler.Requests.ShouldNotContain(static request => request.Message.RequestUri!.AbsolutePath.Contains("/issues/", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Given a Failed transition on an issue, when TransitionAsync is called, then the issue note is posted and the issue is left open")]
    public async Task FailedIssuePostsNoteOnlyAsync()
    {
        var harness = GitLabTicketSyncHarness.Create();
        var transition = GitLabTicketSyncHarness.Transition("acme/app#7", RunStatuses.Failed, InboundTicketKind.Issue);

        await harness.Sync.TransitionAsync(harness.Connection, transition, TestContext.Current.CancellationToken);

        harness.Handler.Requests.Count.ShouldBe(1);
        harness.Handler.Requests[0].Message.Method.ShouldBe(HttpMethod.Post);
        harness.Handler.Requests[0].Message.RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/42/issues/7/notes");
    }

    [Fact(DisplayName = "Given a malformed external id, when TransitionAsync is called, then it throws InvalidOperationException and makes no HTTP calls")]
    public async Task MalformedExternalIdThrowsAsync()
    {
        var harness = GitLabTicketSyncHarness.Create();
        var transition = GitLabTicketSyncHarness.Transition("acme/app", RunStatuses.Succeeded, InboundTicketKind.Issue);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await harness.Sync.TransitionAsync(harness.Connection, transition, TestContext.Current.CancellationToken));

        harness.Handler.Requests.ShouldBeEmpty();
    }
}

file sealed class GitLabTicketSyncHarness()
{
    public required RecordingHandler Handler { get; init; }

    public required GitLabTicketSync Sync { get; init; }

    public required SourceConnection Connection { get; init; }

    public static GitLabTicketSyncHarness Create()
    {
        var (factory, handler) = ProviderTestHarness.CreateFactory();
        var secrets = new FakeSecretResolver { Map = { ["COMUKI_GL_TOKEN"] = "glpat_test" } };
        return new GitLabTicketSyncHarness
        {
            Handler = handler,
            Sync = new GitLabTicketSync(factory, secrets),
            Connection = SourceConnection.Create(
                ProjectId.New(),
                TicketProvider.GitLab,
                "test",
                /*lang=json,strict*/ """{"projectId": 42, "projectPath": "acme/app", "apiTokenEnv": "COMUKI_GL_TOKEN"}""",
                "HOOK",
                "key123",
                DateTimeOffset.UtcNow),
        };
    }

    public static TicketTransition Transition(string externalId, string runStatus, InboundTicketKind kind)
    {
        return new TicketTransition(externalId, ExternalUrl: null, runStatus, new Uri("https://comuki.example/runs/1"), kind);
    }
}
