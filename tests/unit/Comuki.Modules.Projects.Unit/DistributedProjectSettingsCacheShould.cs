using Comuki.Modules.Projects.Application.Settings.DistributedCache;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Primitives;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// <see cref="DistributedProjectSettingsCache"/> over the in-memory
/// <see cref="IDistributedCache"/> double: the contract the
/// StackExchange.Redis binding provides is exercised without spinning a
/// real Redis. The serialization round-trip (UTF-8 JSON via
/// <see cref="System.Text.Json.JsonSerializerOptions"/>) and the
/// in-process change-token firing are the two behaviours the
/// multi-replica cache needs to keep working under the switch.
/// </summary>
public sealed class DistributedProjectSettingsCacheShould
{
    private readonly InMemoryDistributedCache distributedCache = new();
    private readonly DistributedProjectSettingsCache cache;

    public DistributedProjectSettingsCacheShould()
    {
        cache = new DistributedProjectSettingsCache(distributedCache);
    }

    [Fact(DisplayName = "Given an untouched cache, when a project is read, then the answer is null")]
    public void ReturnNullWhenNotCached()
    {
        cache.Get(ProjectId.New()).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a warmed entry, when read back, then the snapshot matches what was stored")]
    public void ReturnWarmedEntry()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), DateTimeOffset.UtcNow);
        cache.Warm(settings);

        var readBack = cache.Get(settings.ProjectId);
        readBack.ShouldNotBeNull();
        readBack.ProjectId.ShouldBe(settings.ProjectId);
        readBack.MaxConcurrent.ShouldBe(settings.MaxConcurrent);
        readBack.Version.ShouldBe(settings.Version);
    }

    [Fact(DisplayName = "Given a cached row, when Refresh stores a new snapshot, then a subsequent Get returns the new value")]
    public void ReplaceEntryOnRefresh()
    {
        var projectId = ProjectId.New();
        var original = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);
        cache.Warm(original);

        var updated = ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow);
        updated.Apply(1, 12, 600, false, false, false, false, null, null, ProjectDomainType.Standard, null, DateTimeOffset.UtcNow);
        cache.Refresh(updated);

        var stored = cache.Get(projectId);
        stored.ShouldNotBeNull();
        stored.Version.ShouldBe(updated.Version);
        stored.MaxConcurrent.ShouldBe(12);
    }

    [Fact(DisplayName = "Given a change token, when Refresh stores a new snapshot, then the token fires")]
    public void FireChangeTokenOnRefresh()
    {
        var projectId = ProjectId.New();
        var token = cache.GetChangeToken(projectId);
        token.HasChanged.ShouldBeFalse();

        cache.Refresh(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        token.HasChanged.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a fired token, when a new token is taken, then it is armed again")]
    public void ArmFreshTokenAfterChange()
    {
        var projectId = ProjectId.New();
        cache.Refresh(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        var nextToken = cache.GetChangeToken(projectId);
        nextToken.HasChanged.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a change token, when the entry is only warmed (read fill), then the token does not fire")]
    public void KeepQuietOnWarm()
    {
        var projectId = ProjectId.New();
        var token = cache.GetChangeToken(projectId);

        cache.Warm(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        token.HasChanged.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a registered callback, when Refresh stores a new snapshot, then the callback runs")]
    public void RunRegisteredCallbackOnRefresh()
    {
        var projectId = ProjectId.New();
        var fired = 0;

        ChangeToken.OnChange(
            () => cache.GetChangeToken(projectId),
            () => fired++);

        cache.Refresh(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        // boundary: callback execution is synchronous for CancellationChangeToken
        fired.ShouldBe(1);
    }

    [Fact(DisplayName = "Given the underlying distributed cache, when Warm stores a snapshot, then the value is JSON-serialised under the cache key")]
    public void StoreJsonBytesUnderProjectKey()
    {
        var projectId = ProjectId.New();
        cache.Warm(ProjectSettings.CreateDefaults(projectId, DateTimeOffset.UtcNow));

        var key = $"projects:settings:{projectId.Value}";
        var snapshot = distributedCache.Snapshot();
        snapshot.ShouldContainKey(key);
        snapshot[key].ShouldNotBeNull();
    }
}
