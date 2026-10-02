using Comuki.Engine.Compute.Environments.Pinning;
using Comuki.Engine.Compute.Environments.Shape;

namespace Comuki.Engine.Compute.Environments;

/// <summary>
/// One catalog entry: a stable <see cref="Id"/> (<c>net10-sdk-bun</c>,
/// <c>cpp-clang-18-linux</c>, …) the operator/host references, an OCI
/// <see cref="Image"/> reference (tagged <c>:latest</c> for dev or
/// digest-pinned <c>@sha256:…</c> for production), the <see cref="Runtime"/>
/// it targets, the <see cref="Publisher"/> shelf it lives on, the restore
/// opcodes it permits (<c>dotnet</c>, <c>bun</c>, …), and the
/// <see cref="ResourceShape"/> it advertises. The Translator + pi runtime
/// already live inside the image — the bundle has no product-repository
/// checkout (worker-environments spec §"Bundle has no product sources").
///
/// <see cref="Image"/> may carry a digest, a tag, or nothing past the
/// repository name. Tag-only entries are refused in <c>Production</c>
/// by <see cref="EnvironmentBundlePinning"/>; dev / stage still allow
/// them as the local-experience path.
/// </summary>
/// <param name="Id">Stable catalog id — referenced by <c>Project.EnvClass</c> and the .comuki/environment.toml <c>class</c> field.</param>
/// <param name="Image">OCI image reference (e.g. <c>ghcr.io/comuki/env/net10-sdk-bun:latest</c>).</param>
/// <param name="Runtime">Runtime the image targets.</param>
/// <param name="Publisher">Shelf the entry belongs to.</param>
/// <param name="RestoreOpcodes">Opcodes the class permits in <c>[restore]</c> of the <c>.comuki/environment.toml</c> (e.g. <c>dotnet</c>, <c>bun</c>).</param>
/// <param name="ResourceShape">Resource shape advertised by the class.</param>
public sealed record EnvironmentBundle(
    string Id,
    string Image,
    EnvironmentRuntime Runtime,
    EnvironmentPublisher Publisher,
    IReadOnlyList<string> RestoreOpcodes,
    EnvironmentResourceShape ResourceShape)
{
    /// <summary>True when the <see cref="Image"/> reference carries a digest (<c>repo@sha256:…</c>); delegates to the pinning single home.</summary>
    public bool HasDigest => EnvironmentBundlePinning.HasDigest(Image);
}
