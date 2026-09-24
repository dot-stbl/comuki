using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// Boot-sequence smoke: the composition this suite starts runs the
/// startup migrations, seeds the platform self-knowledge into
/// <c>memory.memory_facts</c> and hosts the comuki worker registry —
/// the three startup changes land together in
/// <c>HostComposer.ComposeAsync</c>. These tests assert the effects
/// directly (DB row + status endpoint), not just a green boot.
/// </summary>
[Collection(nameof(SmokeIntegrationCollection))]
public sealed class BootShould(SmokeHostServer server)
{
    private readonly SmokeHostServer server = server;

    [Fact(DisplayName = "Given the booted composition, when memory_facts is queried, then the three standing platform.* facts are seeded in the global scope")]
    public async Task SeedPlatformFactsAtBootAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT topic_key, text FROM memory.memory_facts "
            + "WHERE superseded_at IS NULL AND topic_key LIKE 'platform.%' ORDER BY topic_key";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var topics = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            topics.Add(reader.GetString(0));
        }

        topics.ShouldBe(["platform.architecture", "platform.conventions", "platform.identity"]);
    }

    [Fact(DisplayName = "Given the booted composition, when GET /api/v1/workers/background as admin, then the registry reports the converted workers")]
    public async Task ReportBackgroundWorkersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await server.CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/workers/background", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var names = payload.EnumerateArray()
            .Select(static entry => entry.GetProperty("name").GetString())
            .ToList();

        // the three conversions of this batch; the remaining
        // BackgroundServices join the registry in the follow-up batch
        names.ShouldContain("memory-sweep");
        names.ShouldContain("lease-reaper");
        names.ShouldContain("oidc-sweep");

        foreach (var entry in payload.EnumerateArray())
        {
            if (entry.GetProperty("name").GetString() == "oidc-sweep")
            {
                entry.GetProperty("isHealthy").GetBoolean().ShouldBeTrue();
            }
        }
    }
}
