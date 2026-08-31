namespace Comuki.Modules.Identity.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a role assignment row. UUIDv7 like every
/// Identity entity id.
/// </summary>
/// <param name="Value"></param>
public readonly record struct RoleAssignmentId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static RoleAssignmentId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
