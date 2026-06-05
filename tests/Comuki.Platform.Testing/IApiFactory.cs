using System.Net.Http;

namespace Comuki.Platform.Testing;

/// <summary>
/// HTTP client factory contract for API integration tests.
/// </summary>
public interface IApiFactory : IIntegrationFactory
{
    /// <summary>
    /// HTTP client pointing at the running API.
    /// </summary>
    public HttpClient CreateClient();
}
