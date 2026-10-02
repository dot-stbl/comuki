namespace Comuki.Shared.Contracts.Queue;

/// <summary>
/// Claim labels a worker presents when claiming: the item must match
/// the environment class the worker was scaled for, the pinned profiles
/// git ref, and the profile key. <see cref="Image"/> is diagnostics-only
/// — the claim SQL does NOT filter on it; the catalog resolves the image
/// from the class at compute start (add-worker-environments spec
/// §"claim by env class"). Workers present <see cref="EnvClass"/> from
/// <c>COMUKI_ENV_CLASS</c>.
/// </summary>
/// <param name="Image">Diagnostics echo — NOT a claim-match key; the catalog is the image source.</param>
/// <param name="ProfilesRef">Pinned profiles git ref the worker cloned.</param>
/// <param name="ProfileKey">Control-plane profile (role) the worker runs.</param>
/// <param name="EnvClass">Environment class (catalog id) the worker was scaled for.</param>
public sealed record WorkItemLabels(string Image, string ProfilesRef, string ProfileKey, string EnvClass);
