namespace Comuki.Modules.Costs.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a usage event. Entity ids are UUIDv7
/// (<see cref="Guid.CreateVersion7"/>): time-ordered, stored as Postgres
/// <c>uuid</c>, exposed as strings.
/// </summary>
/// <param name="Value"></param>
public readonly record struct UsageEventId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static UsageEventId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
