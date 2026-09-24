using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Integration.RedisCache;

/// <summary>
/// <see cref="DistributedProjectSettingsCache"/> end-to-end against a
/// real Testcontainers Redis (the exact wire format the host uses in
/// multi-replica deploys). The <see cref="IDistributedCache"/> is the
/// production <c>Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache</c>
/// resolved through DI — the integration suite exercises the same code
/// path the host uses. Two scenarios: a single host's Warm → Get round
/// trip, and a cross-instance Get that resolves a second
/// <see cref="IDistributedCache"/> through a second
/// <see cref="StackExchange.Redis.IConnectionMultiplexer"/> — settings
/// writes on replica A must be readable on replica B without waiting for
/// the per-replica refresher. The container itself is shared across every
/// test in <see cref="RedisCollection"/>; only the cheap in-process DI
/// containers below are built per test.
/// </summary>
[Collection(nameof(RedisCollection))]
public sealed class DistributedProjectSettingsCacheRedisShould(RedisFixture fixture) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string InstanceName = "comuki-test:";

    // boundary: assigned in InitializeAsync (IAsyncLifetime) before any [Fact] runs
    private ServiceProvider writerProvider = null!;
    private ServiceProvider readerProvider = null!;
    private IDistributedCache writerCache = null!;
    private IDistributedCache readerCache = null!;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        writerProvider = BuildProvider(fixture.ConnectionString);
        readerProvider = BuildProvider(fixture.ConnectionString);

        writerCache = writerProvider.GetRequiredService<IDistributedCache>();
        readerCache = readerProvider.GetRequiredService<IDistributedCache>();

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await writerProvider.DisposeAsync();
        await readerProvider.DisposeAsync();
    }

    [Fact(DisplayName = "Given a fresh Redis, when Warm stores a snapshot and Get reads it back, then the deserialised row matches the original")]
    public void WarmAndGetRoundTrip()
    {
        var cache = new DistributedProjectSettingsCache(writerCache);
        var projectId = ProjectId.New();
        var original = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);

        cache.Warm(original);
        var readBack = cache.Get(projectId);

        readBack.ShouldNotBeNull();
        readBack.ProjectId.ShouldBe(projectId);
        readBack.MaxConcurrent.ShouldBe(ProjectSettings.DefaultMaxConcurrent);
        readBack.DomainType.ShouldBe(ProjectDomainType.Standard);
        readBack.Version.ShouldBe(original.Version);
    }

    [Fact(DisplayName = "Given a snapshot written through one multiplexer, when another replica's connection reads the same key, then the values agree")]
    public void CrossInstanceRead()
    {
        var writer = new DistributedProjectSettingsCache(writerCache);
        var reader = new DistributedProjectSettingsCache(readerCache);
        var projectId = ProjectId.New();

        var updated = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);
        updated.Apply(2, 16, 900, true, false, false, false, 10_000_000, 50_000_000, ProjectDomainType.Hybrid, /*lang=json,strict*/ "{\"gl-mt\":\"glang\"}", DateTimeOffset.UtcNow);

        writer.Refresh(updated);

        var readBack = reader.Get(projectId);

        readBack.ShouldNotBeNull();
        readBack.MaxConcurrent.ShouldBe(16);
        readBack.ApproveRequired.ShouldBeTrue();
        readBack.DomainType.ShouldBe(ProjectDomainType.Hybrid);
        readBack.SoftBudgetUsdMicros.ShouldBe(10_000_000);
        readBack.HardBudgetUsdMicros.ShouldBe(50_000_000);
        readBack.CustomDomainTypesJson.ShouldBe(/*lang=json,strict*/ "{\"gl-mt\":\"glang\"}");
    }

    [Fact(DisplayName = "Given the cache, when Refresh replaces the snapshot and the change token is taken, then the token has fired")]
    public void RefreshFiresChangeToken()
    {
        var cache = new DistributedProjectSettingsCache(writerCache);
        var projectId = ProjectId.New();

        var token = cache.GetChangeToken(projectId);
        token.HasChanged.ShouldBeFalse();

        cache.Refresh(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        token.HasChanged.ShouldBeTrue();
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = async () =>
                await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(
                    StackExchange.Redis.ConfigurationOptions.Parse(connectionString));
            options.InstanceName = InstanceName;
        });
        return services.BuildServiceProvider();
    }
}
