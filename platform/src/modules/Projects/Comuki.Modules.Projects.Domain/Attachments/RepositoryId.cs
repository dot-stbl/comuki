namespace Comuki.Modules.Projects.Domain.Attachments;

/// <summary>
/// Projects-side value handle on the sibling Repositories module's
/// <c>Repository</c> aggregate id. The Projects module never references
/// <c>Comuki.Modules.Repositories</c> — modular-monolith law 3 keeps
/// siblings isolated — but a <see cref="ProjectRepositoryAttachment"/>
/// needs to point at a Repository row, so the value lives in this module
/// as an opaque <see cref="Guid"/> and the host composition root translates
/// between the two at the boundary (same law-3 pattern by which
/// <c>DomainTypeAdmission</c> stores intake source keys as plain strings,
/// see its doc comment).
/// <para>
/// The two <see cref="Guid"/> values are the same bytes on the wire and in
/// the database; the type split is the seam that lets both modules stay
/// independently compilable.
/// </para>
/// UUIDv7 so the registry's "list by repository" query benefits from the
/// monotonic order — newer attachments sort to the end, narrow B-tree.
/// </summary>
/// <param name="Value">The raw UUIDv7 — the Repositories module's id value at the same bytes.</param>
public readonly record struct RepositoryId(Guid Value)
{
    /// <summary>Generates a new UUIDv7 identifier.</summary>
    /// <returns>A fresh id.</returns>
    public static RepositoryId New()
    {
        return new(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Value.ToString();
    }
}
