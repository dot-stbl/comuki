using Testcontainers.Redis;
using Xunit;

namespace Comuki.Modules.Projects.Integration.RedisCache;

/// <summary>
/// One Redis container shared by every test in <see cref="RedisCollection"/>
/// (testing-integration.md §2/§5 — one shared instance per run, not
/// container-per-test). <c>.WithReuse(true)</c> lets a local dev loop skip
/// the teardown/restart cost across repeated runs.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:7-alpine")
        .WithReuse(true)
        .Build();

    /// <summary>Connection string of the shared container; valid only after <see cref="InitializeAsync"/>.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
        ConnectionString = container.GetConnectionString();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }
}
