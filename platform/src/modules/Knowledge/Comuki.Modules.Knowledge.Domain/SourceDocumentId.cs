namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// Strong-typed identifier of a <see cref="SourceDocument"/>. UUIDv7
/// (<see cref="Guid.CreateVersion7()"/>); stored as Postgres <c>uuid</c>.
/// </summary>
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
