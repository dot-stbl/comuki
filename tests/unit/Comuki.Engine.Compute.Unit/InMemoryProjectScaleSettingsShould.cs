using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Settings;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="InMemoryProjectScaleSettings"/>: defaults from
/// options, per-project overrides, override isolation between projects.
/// </summary>
public sealed class InMemoryProjectScaleSettingsShould
{
    private static ScaleSupervisorOptions CreateOptions()
    {
        return new()
        {
            MinIdle = 2,
            MaxConcurrent = 6,
            IdleTtl = TimeSpan.FromMinutes(5),
        };
    }

    [Fact]
    public void ReturnOptionDefaultsWhenNoOverrideIsSet()
    {
        var settings = new InMemoryProjectScaleSettings(Microsoft.Extensions.Options.Options.Create(CreateOptions()));

        var effective = settings.Get(ProjectId.New());

        effective.MinIdle.ShouldBe(2);
        effective.MaxConcurrent.ShouldBe(6);
        effective.IdleTtl.ShouldBe(TimeSpan.FromMinutes(5));
        effective.WorkerImage.ShouldBeNull();
        effective.ProfilesGitRef.ShouldBeNull();
    }

    [Fact]
    public void ReturnOverrideOnceSet()
    {
        var projectId = ProjectId.New();
        var settings = new InMemoryProjectScaleSettings(Microsoft.Extensions.Options.Options.Create(CreateOptions()));

        settings.Override(projectId, new ProjectScaleSettings(0, 1, TimeSpan.FromMinutes(1), WorkerImage: "custom/worker:1"));

        var effective = settings.Get(projectId);
        effective.MinIdle.ShouldBe(0);
        effective.MaxConcurrent.ShouldBe(1);
        effective.IdleTtl.ShouldBe(TimeSpan.FromMinutes(1));
        effective.WorkerImage.ShouldBe("custom/worker:1");
    }

    [Fact]
    public void KeepOtherProjectsOnDefaultsAfterOverride()
    {
        var overridden = ProjectId.New();
        var untouched = ProjectId.New();
        var settings = new InMemoryProjectScaleSettings(Microsoft.Extensions.Options.Options.Create(CreateOptions()));

        settings.Override(overridden, new ProjectScaleSettings(0, 1, TimeSpan.FromMinutes(1)));

        settings.Get(overridden).MaxConcurrent.ShouldBe(1);
        settings.Get(untouched).MaxConcurrent.ShouldBe(6);
    }
}
