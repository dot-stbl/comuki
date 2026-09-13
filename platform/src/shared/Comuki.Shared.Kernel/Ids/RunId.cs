namespace Comuki.Shared.Kernel.Ids;

/// <summary>
/// Strong-typed identifier of a run — one goal from intake (ticket / chat).
/// Entity ids are UUIDv7 (<see cref="Guid.CreateVersion7()"/>): time-ordered,
/// stored as Postgres <c>uuid</c>, exposed to the API as strings.
/// </summary>
/// <param name="Value">The underlying UUIDv7 guid — time-ordered, wire-exposed as a string.</param>
public readonly record struct RunId(Guid Value)
{
    public static RunId New()
    {
        return new(Guid.CreateVersion7());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}
