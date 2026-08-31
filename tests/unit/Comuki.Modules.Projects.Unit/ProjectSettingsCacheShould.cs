using Comuki.Modules.Projects.Application.Settings;
using Comuki.Modules.Projects.Domain.Settings;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Primitives;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Projects.Unit;

/// <summary>
/// Snapshot cache semantics (the live-reload core): reads are memory-only,
/// writes replace the entry AND fire the per-project change token, read
/// fills do not look like changes, and the TTL (driven by the cache clock —
/// hence the fake) bounds staleness when nothing refreshes.
/// </summary>
public sealed class ProjectSettingsCacheShould
{
    private readonly SettableSystemClock clock = new() { UtcNow = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero) };

    private readonly ProjectSettingsCache cache;

    public ProjectSettingsCacheShould()
    {
        cache = new ProjectSettingsCache(new MemoryCache(new MemoryCacheOptions { Clock = clock }));
    }

    [Fact(DisplayName = "Given an untouched cache, when a project is read, then the answer is null (fall back to defaults)")]
    public void ReturnNullWhenNotCached()
    {
        cache.Get(ProjectId.New()).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a warmed entry, when read before the TTL, then the snapshot is returned")]
    public void ReturnWarmedEntry()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), clock.UtcNow);
        cache.Warm(settings);

        cache.Get(settings.ProjectId).ShouldBeSameAs(settings);
    }

    [Fact(DisplayName = "Given a warmed entry, when the clock passes the TTL without a refresh, then the entry lapses")]
    public void ExpireEntryAfterTtl()
    {
        var settings = ProjectSettings.CreateDefaults(ProjectId.New(), clock.UtcNow);
        cache.Warm(settings);
        clock.UtcNow += ProjectSettingsCache.EntryTtl + TimeSpan.FromSeconds(1);

        cache.Get(settings.ProjectId).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a cached row, when Refresh stores a new snapshot, then readers see it immediately")]
    public void ReplaceEntryOnRefresh()
    {
        var projectId = ProjectId.New();
        var original = ProjectSettings.CreateDefaults(projectId, clock.UtcNow);
        cache.Warm(original);

        var updated = ProjectSettings.CreateDefaults(projectId, clock.UtcNow);
        updated.Apply(1, 12, 600, false, false, false, false, clock.UtcNow);
        cache.Refresh(updated);

        cache.Get(projectId).ShouldBeSameAs(updated);
    }

    [Fact(DisplayName = "Given a change token, when Refresh stores a new snapshot, then the token fires")]
    public void FireChangeTokenOnRefresh()
    {
        var projectId = ProjectId.New();
        var token = cache.GetChangeToken(projectId);
        token.HasChanged.ShouldBeFalse();
        token.ActiveChangeCallbacks.ShouldBeTrue();

        cache.Refresh(ProjectSettings.CreateDefaults(projectId, clock.UtcNow));

        token.HasChanged.ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a fired token, when a new token is taken, then it is armed again")]
    public void ArmFreshTokenAfterChange()
    {
        var projectId = ProjectId.New();
        cache.Refresh(ProjectSettings.CreateDefaults(projectId, clock.UtcNow));

        var nextToken = cache.GetChangeToken(projectId);
        nextToken.HasChanged.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a change token, when the entry is only warmed (read fill), then the token does not fire")]
    public void KeepQuietOnWarm()
    {
        var projectId = ProjectId.New();
        var token = cache.GetChangeToken(projectId);

        cache.Warm(ProjectSettings.CreateDefaults(projectId, clock.UtcNow));

        token.HasChanged.ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a registered callback, when Refresh stores a new snapshot, then the callback runs")]
    public void RunRegisteredCallbackOnRefresh()
    {
        var projectId = ProjectId.New();
        var fired = 0;

        _ = ChangeToken.OnChange(
            () => cache.GetChangeToken(projectId),
            () => fired++);

        cache.Refresh(ProjectSettings.CreateDefaults(projectId, clock.UtcNow));

        // boundary: callback execution is synchronous for CancellationChangeToken
        fired.ShouldBe(1);
    }
}

/// <summary>Deterministic cache clock — advances only when the test says so.</summary>
internal sealed class SettableSystemClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; }
}
