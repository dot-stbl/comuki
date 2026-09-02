using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Intake;

/// <summary>
/// The native ticket surface: creates ticket + run in one call, honors
/// the one-live-run lock, and demands <c>run:create</c>.
/// </summary>
[Collection(nameof(IntakeHostCollection))]
public sealed class NativeTicketsShould(HostIntakeServer server)
{
    private readonly HostIntakeServer server = server;

    [Fact(DisplayName = "Given an authenticated caller, when a native ticket is posted, then ticket and run are created and a repeat for the same external id conflicts")]
    public async Task CreateNativeTicketWithRunAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = await server.CreateBrowserClientAsync();
        var projectId = Shared.Kernel.Ids.ProjectId.New().Value;

        var create = await browser.PostAsJsonAsync(
            "/api/v1/tickets",
            new { projectId, title = "Ship the release", body = "Bump and tag.", externalId = "native-ship-1", author = "ops" },
            cancellationToken);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var view = await HostIntakeFiles.ReadJsonAsync(create);
        view.GetProperty("status").GetString().ShouldBe("Claimed");
        view.GetProperty("runId").GetString().ShouldNotBeNull();

        // one live run per issue — the repeat conflicts
        var repeat = await browser.PostAsJsonAsync(
            "/api/v1/tickets",
            new { projectId, title = "Ship the release again", body = string.Empty, externalId = "native-ship-1" },
            cancellationToken);
        repeat.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "Given an anonymous caller, when a native ticket is posted, then it is 401 (run:create demanded)")]
    public async Task RefuseAnonymousNativeTicketAsync()
    {
        using var anonymous = server.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/tickets",
            new { projectId = Guid.NewGuid(), title = "nope" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "Given an anonymous caller, when the inbox claim is called, then it is 401 (intake:claim demanded)")]
    public async Task RefuseAnonymousClaimAsync()
    {
        using var anonymous = server.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/inbox/claim",
            new { ticketId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
