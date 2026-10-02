namespace Comuki.Engine.Compute.Environments.Shape;

/// <summary>
/// Resource shape advertised by a catalog entry. All three fields are
/// optional: a missing <see cref="Cpus"/> or <see cref="Memory"/> means
/// the class defers the sizing decision to the host (Docker: omitted; k8s:
/// no <c>requests</c>/<c>limits</c> applied). <see cref="Gpu"/> is opt-in
/// — workers advertise the class only when the host can satisfy it.
/// </summary>
/// <param name="Cpus">CPU core count the class needs to run (null → host decides).</param>
/// <param name="Memory">Memory the class needs (free-form string such as "8Gi" / "16Gi"; null → host decides).</param>
/// <param name="Gpu">True when the class needs a GPU host (e.g. UE / ML training); false otherwise.</param>
public sealed record EnvironmentResourceShape(int? Cpus, string? Memory, bool Gpu);
