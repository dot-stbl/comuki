namespace Comuki.Modules.Repositories.Domain.Ids;

/// <summary>
/// Strong-typed identifier of a registered git repository aggregate
/// landing in workstream 2. UUIDv7 so the registry list query benefits
/// from the monotonic order — newly registered repositories sort to the
/// end, which keeps the index B-tree narrow and avoids page splits when
/// a deployment registers dozens of repositories back-to-back.
/// </summary>
/// <param name="Value">The raw UUIDv7.</param>
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
