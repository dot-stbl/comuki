using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>A running worker as listed by the provider, with its match labels.</summary>
/// <param name="Id"></param>
/// <param name="ProviderRef"></param>
/// <param name="ProfileKey"></param>
/// <param name="Image"></param>
/// <param name="ProfilesGitRef"></param>
public sealed record WorkerInfo(
    WorkerId Id,
    string ProviderRef,
    string ProfileKey,
    string Image,
    string ProfilesGitRef);
