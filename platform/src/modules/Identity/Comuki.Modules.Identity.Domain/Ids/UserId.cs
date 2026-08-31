namespace Comuki.Modules.Identity.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a user account. Entity ids are UUIDv7
/// (<see cref="Guid.CreateVersion7"/>): time-ordered, stored as Postgres
/// <c>uuid</c>, exposed to the API as strings. Lives in the Identity module
/// (not Shared.Kernel) — other modules learn about users through contracts.
/// </summary>
/// <param name="Value"></param>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static UserId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
