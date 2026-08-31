namespace Comuki.Shared.Kernel.Ids;

/// <summary>
/// Strong-typed identifier of a project — the scope unit for runs, work items,
/// settings and role assignments.
/// </summary>
/// <param name="Value"></param>
public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
