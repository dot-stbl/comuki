using Comuki.Shared.Kernel.Ids;

namespace Comuki.Modules.Projects.Application.Settings.Cache;

/// <summary>
/// Cache key scheme shared by the in-process (<see cref="ProjectSettingsCache"/>)
/// and distributed (<c>DistributedProjectSettingsCache</c>) snapshot
/// caches — one place, no format literals scattered across the two
/// implementations. The Redis instance-name prefix (<c>Redis:InstanceName</c>)
/// is layered on top of these keys by
/// <c>Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache</c>, so the
/// wire-format key ends up <c>{InstanceName}projects:settings:{projectId}</c>.
/// </summary>
internal static class SettingsCacheKeys
{
    public static string Key(ProjectId projectId)
    {
        return $"projects:settings:{projectId.Value}";
    }
}
