namespace Comuki.Shared.Kernel.Ids;

/// <summary>
/// Strong-typed identifier of a worker container instance (one running
/// Comuki worker image). Distinct from the work item the worker claims.
/// </summary>
/// <param name="Value"></param>
public readonly record struct WorkerId(Guid Value)
{
    public static WorkerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
