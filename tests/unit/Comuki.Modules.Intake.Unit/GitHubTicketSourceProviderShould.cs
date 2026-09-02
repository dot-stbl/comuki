using System.Text;
using Comuki.Modules.Intake.Application.Ports.Sync;
using Comuki.Modules.Intake.Domain.Connections;
using Comuki.Modules.Intake.Domain.Tickets;
using Comuki.Modules.Intake.Infrastructure.Providers;
using Comuki.Modules.Intake.Infrastructure.Providers.GitHub;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// GitHub provider over the fake handler: the catalog fetch hits the
/// issues endpoint with auth, PRs are filtered out, the delivery id
/// prefers the header, and verification resolves the secret from the
/// connection's env ref.
/// </summary>
public sealed class GitHubTicketSourceProviderShould
{
    private static readonly ProjectId projectId = ProjectId.New();
    private static readonly DateTimeOffset now = DateTimeOffset.UtcNow;

    [Fact(DisplayName = "Given a connection, when the catalog is fetched, then the issues endpoint is called with the bearer token and PRs are filtered")]
    public async Task FetchCatalogThroughRefitClientAsync()
    {
        var (factory, handler) = ProviderTestHarness.CreateFactory();
        var pageJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-issues-page.json"), TestContext.Current.CancellationToken);
        handler.Respond = request => ProviderTestHarness.Json(pageJson);

        var secrets = new FakeSecretResolver { Map = { ["COMUKI_GH_TOKEN"] = "ghp_test" } };
        var provider = new GitHubTicketSourceProvider(factory, secrets, TimeProvider.System);
        var connection = Connection(/*lang=json,strict*/ """{"owner": "dot-stbl", "repo": "comuki", "apiTokenEnv": "COMUKI_GH_TOKEN"}""");

        var tickets = await provider.FetchCatalogAsync(connection, page: 1, TestContext.Current.CancellationToken);

        tickets.Count.ShouldBe(1);
        tickets[0].ExternalId.ShouldBe("dot-stbl/comuki#482");
        tickets[0].Author.ShouldBe("bob");

        var request = handler.Requests.Single();
        request.Message.RequestUri!.AbsolutePath.ShouldBe("/repos/dot-stbl/comuki/issues");
        request.Message.RequestUri.Query.ShouldContain("state=open");
        request.Message.RequestUri.Query.ShouldContain("page=1");
        request.Message.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Message.Headers.Authorization.Parameter.ShouldBe("ghp_test");
    }

    [Fact(DisplayName = "Given a delivery with the GitHub delivery header, when the delivery id is derived, then the header wins; without it the body hash answers")]
    public void PreferDeliveryHeader()
    {
        var (factory, _) = ProviderTestHarness.CreateFactory();
        var provider = new GitHubTicketSourceProvider(factory, new FakeSecretResolver(), TimeProvider.System);
        var body = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"action":"opened"}""");

        var withHeader = new WebhookDelivery(
            body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-GitHub-Delivery"] = "abc-123" },
            ProviderTestHarness.NoQuery);
        var withoutHeader = new WebhookDelivery(body, ProviderTestHarness.NoHeaders, ProviderTestHarness.NoQuery);

        provider.DeliveryIdOf(withHeader).ShouldBe("abc-123");
        provider.DeliveryIdOf(withoutHeader).ShouldBe(ProviderDeliveryIds.BodyHash(body));
    }

    [Fact(DisplayName = "Given a signed delivery, when verified against the connection secret, then it passes; a wrong signature is rejected")]
    public async Task VerifySignatureThroughEnvSecretAsync()
    {
        var (factory, _) = ProviderTestHarness.CreateFactory();
        var secrets = new FakeSecretResolver { Map = { ["COMUKI_GH_HOOK"] = "whsec_hook" } };
        var provider = new GitHubTicketSourceProvider(factory, secrets, TimeProvider.System);
        var connection = Connection("{}");
        var payload = await GitHubPayloadMapperShould.FixtureAsync("github-issue-opened.json");

        var signature = "sha256=" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData("whsec_hook"u8.ToArray(), payload.ToArray())).ToLowerInvariant();
        var good = new WebhookDelivery(
            payload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Hub-Signature-256"] = signature },
            ProviderTestHarness.NoQuery);

        provider.VerifySignature(connection, good).ShouldBeTrue();
        provider.VerifySignature(connection, good with { Headers = ProviderTestHarness.NoHeaders }).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given an opened payload, when normalized through the provider, then the ticket maps (integration of provider + mapper)")]
    public async Task NormalizeThroughProviderAsync()
    {
        var (factory, _) = ProviderTestHarness.CreateFactory();
        var provider = new GitHubTicketSourceProvider(factory, new FakeSecretResolver(), TimeProvider.System);
        var connection = Connection("{}");
        var payload = await GitHubPayloadMapperShould.FixtureAsync("github-issue-opened.json");

        var ticket = provider.Normalize(new WebhookDelivery(payload, ProviderTestHarness.NoHeaders, ProviderTestHarness.NoQuery), connection);

        ticket.ShouldNotBeNull();
        ticket.ProjectId.ShouldBe(projectId);
        ticket.ExternalId.ShouldBe("dot-stbl/comuki#481");
    }

    private static SourceConnection Connection(string settingsJson)
    {
        return SourceConnection.Create(projectId, TicketProvider.GitHub, "test", settingsJson, "COMUKI_GH_HOOK", "key123", now);
    }
}
