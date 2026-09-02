using Comuki.Modules.Intake.Application.Ports;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers.GitLab;
using Comuki.Modules.Intake.Infrastructure.Providers.Jira;
using Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// GitLab / Yandex Tracker / Jira catalog fetches through the fake
/// handler — auth headers, request shapes and the ticket mapping of the
/// search responses.
/// </summary>
public sealed class TrackerCatalogsShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given a GitLab connection, when the catalog is fetched, then the project issues endpoint is called with the private token")]
    public async Task FetchGitLabCatalogAsync()
    {
        var (factory, handler) = ProviderTestHarness.CreateFactory();
        handler.Respond = static request => ProviderTestHarness.Json(
                                 /*lang=json,strict*/
                                 """
            [
              {
                "iid": 12,
                "title": "Deploy fails at midnight",
                "description": "dies at 00:00",
                "web_url": "https://gitlab.com/acme/platform/-/issues/12",
                "author": { "username": "bob" },
                "labels": ["infra"]
              }
            ]
            """);

        var secrets = new FakeSecretResolver { Map = { ["COMUKI_GL_TOKEN"] = "glpat-test" } };
        var provider = new GitLabTicketSourceProvider(factory, secrets, TimeProvider.System);

        var tickets = await provider.FetchCatalogAsync(GitLabConnection(), page: 1, TestContext.Current.CancellationToken);

        tickets.Count.ShouldBe(1);
        tickets[0].ExternalId.ShouldBe("acme/platform#12");
        tickets[0].Labels.ShouldBe(["infra"]);

        var request = handler.Requests.Single();
        request.Message.RequestUri!.AbsolutePath.ShouldBe("/api/v4/projects/12345/issues");
        request.Message.Headers.GetValues("PRIVATE-TOKEN").Single().ShouldBe("glpat-test");
    }

    [Fact(DisplayName = "Given a Yandex Tracker connection, when the catalog is fetched, then the search endpoint is called with the OAuth and org headers")]
    public async Task FetchYandexTrackerCatalogAsync()
    {
        var (factory, handler) = ProviderTestHarness.CreateFactory();
        var pageJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tracker-issues-search.json"), TestContext.Current.CancellationToken);
        handler.Respond = request => ProviderTestHarness.Json(pageJson);

        var secrets = new FakeSecretResolver { Map = { ["COMUKI_YT_TOKEN"] = "yoauth-test" } };
        var provider = new YandexTrackerTicketSourceProvider(factory, secrets, TimeProvider.System);

        var tickets = await provider.FetchCatalogAsync(TrackerConnection(), page: 1, TestContext.Current.CancellationToken);

        tickets.Count.ShouldBe(2);
        tickets[0].ExternalId.ShouldBe("COMUKI-5");
        tickets[1].ExternalId.ShouldBe("COMUKI-6");

        var request = handler.Requests.Single();
        request.Message.RequestUri!.AbsolutePath.ShouldBe("/v2/issues/_search");
        request.Message.Headers.Authorization!.Scheme.ShouldBe("OAuth");
        request.Message.Headers.GetValues("X-Org-Id").Single().ShouldBe("8283118");
        request.Body.ShouldContain("COMUKI");
    }

    [Fact(DisplayName = "Given a Jira connection, when the catalog is fetched, then the search endpoint is called with the JQL and basic auth")]
    public async Task FetchJiraCatalogAsync()
    {
        var (factory, handler) = ProviderTestHarness.CreateFactory();
        var pageJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jira-search.json"), TestContext.Current.CancellationToken);
        handler.Respond = request => ProviderTestHarness.Json(pageJson);

        var secrets = new FakeSecretResolver { Map = { ["COMUKI_JIRA_CRED"] = "dev@example.com:api-token" } };
        var provider = new JiraTicketSourceProvider(factory, secrets, TimeProvider.System);

        var tickets = await provider.FetchCatalogAsync(JiraConnection(), page: 1, TestContext.Current.CancellationToken);

        tickets.Count.ShouldBe(1);
        tickets[0].ExternalId.ShouldBe("COM-10");
        tickets[0].Url.ShouldBe("https://acme.atlassian.net/browse/COM-10");

        var request = handler.Requests.Single();
        request.Message.RequestUri!.AbsolutePath.ShouldBe("/rest/api/2/search");
        request.Message.RequestUri.Query.ShouldContain("jql=");
        request.Message.RequestUri.Query.ShouldContain("startAt=0");
        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("dev@example.com:api-token"));
        request.Message.Headers.Authorization!.Scheme.ShouldBe("Basic");
        request.Message.Headers.Authorization.Parameter.ShouldBe(expected);
    }

    [Fact(DisplayName = "Given a Jira delivery with the secret query param, when verified, then the settings-named param is compared")]
    public void VerifyJiraSecretParam()
    {
        var (factory, _) = ProviderTestHarness.CreateFactory();
        var secrets = new FakeSecretResolver { Map = { ["COMUKI_JIRA_HOOK"] = "hook-secret" } };
        var provider = new JiraTicketSourceProvider(factory, secrets, TimeProvider.System);

        var delivery = new WebhookDelivery(
            "{}"u8.ToArray(),
            ProviderTestHarness.NoHeaders,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["secret"] = "hook-secret" });

        provider.VerifySignature(JiraConnection(), delivery).ShouldBeTrue();
        provider.VerifySignature(JiraConnection(), delivery with { Query = ProviderTestHarness.NoQuery }).ShouldBeFalse();
    }

    private static SourceConnection GitLabConnection()
    {
        return SourceConnection.Create(
            projectId,
            TicketProvider.GitLab,
            "test",
                                 /*lang=json,strict*/
                                 """{"projectId": 12345, "projectPath": "acme/platform", "apiTokenEnv": "COMUKI_GL_TOKEN"}""",
            "COMUKI_GL_HOOK",
            "key123",
            now);
    }

    private static SourceConnection TrackerConnection()
    {
        return SourceConnection.Create(
            projectId,
            TicketProvider.YandexTracker,
            "test",
                                 /*lang=json,strict*/
                                 """{"queue": "COMUKI", "orgId": "8283118", "apiTokenEnv": "COMUKI_YT_TOKEN"}""",
            "COMUKI_YT_HOOK",
            "key123",
            now);
    }

    private static SourceConnection JiraConnection()
    {
        return SourceConnection.Create(
            projectId,
            TicketProvider.Jira,
            "test",
                                 /*lang=json,strict*/
                                 """{"site": "https://acme.atlassian.net", "project": "COM", "apiTokenEnv": "COMUKI_JIRA_CRED"}""",
            "COMUKI_JIRA_HOOK",
            "key123",
            now);
    }
}
