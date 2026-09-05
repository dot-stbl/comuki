using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Comuki.Host.Integration.Smoke;

/// <summary>
/// Knowledge endpoint coverage (S10 #9):
/// <list type="bullet">
///   <item><c>POST /api/v1/knowledge/ingest</c> with the bootstrap admin
///     returns the source document id + chunks-written count.</item>
///   <item><c>GET /api/v1/projects/{id}/runs/{runId}/artifacts</c> returns
///     an empty items list for an unseen run (the smoke covers the same
///     end-to-end surface as <c>S28</c>'s dedicated integration test, but
///     validates the full host composition — pgvector + MinIO — is wired).</item>
/// </list>
/// </summary>
public sealed class KnowledgeShould(SmokeHostServer server) : IClassFixture<SmokeHostServer>
{
    private readonly SmokeHostServer server = server;

    [Fact(DisplayName = "Given the bootstrap admin, when POST /api/v1/knowledge/ingest, then 200 with the source document id")]
    public async Task IngestReturnsSourceDocumentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await server.CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/knowledge/ingest",
            new
            {
                projectId = (Guid?)null,
                title = "smoke-ingest-title",
                source = "upload",
                sourceRef = $"smoke-ref-{Guid.NewGuid():N}",
                mimeType = "text/plain",
                text = "the quick brown fox jumps over the lazy dog",
            },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var sourceDocumentId = payload.GetProperty("sourceDocumentId").GetString();
        sourceDocumentId.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParse(sourceDocumentId, out _).ShouldBeTrue();
        payload.GetProperty("chunksWritten").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact(DisplayName = "Given the bootstrap admin, when GET /api/v1/projects/{id}/runs/{runId}/artifacts, then 200 with an empty items list")]
    public async Task ArtifactsForUnseenRunReturnsEmptyPageAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await server.CreateAdminClientAsync();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/runs/{Guid.NewGuid()}/artifacts",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        payload.GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}
