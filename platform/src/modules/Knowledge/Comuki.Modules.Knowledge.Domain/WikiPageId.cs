namespace Comuki.Modules.Knowledge.Domain;

/// <summary>
/// Strong-typed identifier of a Wiki page. UUIDv7
/// (<see cref="Guid.CreateVersion7()"/>); stored as Postgres <c>uuid</c>.
/// Carried on <see cref="SourceDocument"/> when <see cref="SourceKind.Wiki"/>
/// is the document's origin kind; null for every other kind.
/// </summary>
/// <param name="Value"></param>
public readonly record struct WikiPageId(Guid Value)
{
    /// <summary>Creates a fresh UUIDv7 id.</summary>
    public static WikiPageId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
