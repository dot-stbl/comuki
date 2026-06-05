using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Comuki.Platform.Testing;

/// <summary>
/// Base class for integration tests. Handles factory lifecycle and DI access.
/// </summary>
/// <typeparam name="TFactory">The concrete factory type.</typeparam>
public abstract class IntegrationTestBase<TFactory>(TFactory factory) : IAsyncLifetime
    where TFactory : class, IIntegrationFactory
{
    protected TFactory Factory { get; } = factory;

    /// <summary>
    /// Resolves a scoped service from the test application's DI container.
    /// Each call creates a new scope.
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        using var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public virtual ValueTask InitializeAsync() => new(Factory.ResetAsync());
    public virtual ValueTask DisposeAsync() => default;
}
