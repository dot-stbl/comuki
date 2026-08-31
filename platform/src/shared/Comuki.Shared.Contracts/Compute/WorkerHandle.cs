using Comuki.Shared.Kernel.Ids;

namespace Comuki.Shared.Contracts.Compute;

/// <summary>A started worker. <see cref="ProviderRef"/> is the container id / job name.</summary>
/// <param name="Id"></param>
/// <param name="ProviderRef"></param>
public sealed record WorkerHandle(WorkerId Id, string ProviderRef);
