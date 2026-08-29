using Microsoft.Extensions.DependencyInjection;

namespace Comuki.Platform.Testing;

/// <summary>
/// Common contract for integration test factories.
/// All WebApplicationFactory-based fixtures implement this.
/// </summary>
public interface IIntegrationFactory
{
    /// <summary>
    /// DI container of the started application.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Resets state between tests: database cleanup, cache eviction, etc.
    /// </summary>
    public Task ResetAsync();
}
