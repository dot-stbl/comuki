namespace Comuki.Modules.Memory.Domain.Knowledge;

/// <summary>
/// Strong-typed identifier of a <see cref="SourceDocument"/>. UUIDv7
/// (<see cref="Guid.CreateVersion7"/>); stored as Postgres <c>uuid</c>.
/// </summary>
/// <param name="Value"></param>
public readonly record struct SourceDocumentId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static SourceDocumentId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
