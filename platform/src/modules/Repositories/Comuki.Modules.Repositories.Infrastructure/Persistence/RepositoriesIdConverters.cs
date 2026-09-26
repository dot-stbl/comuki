using Comuki.Modules.Repositories.Domain.Ids;
using Comuki.Modules.Repositories.Domain.Repositories;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Comuki.Modules.Repositories.Infrastructure.Persistence;

/// <summary>
/// Value converters mapping the module's own ids to <c>uuid</c> columns
/// (the database stores a row's id as a Postgres <c>uuid</c>; the
/// repository registry uses UUIDv7 via <see cref="RepositoryId.New"/>
/// so newly registered repositories sort to the end of the list — narrow
/// B-tree, no page splits on a back-to-back bulk registration).
/// </summary>
public static class RepositoriesIdConverters
{
    /// <summary><see cref="RepositoryId"/> uuid converter.</summary>
    public static readonly ValueConverter<RepositoryId, Guid> RepositoryIdToUuid = new(
        static id => id.Value,
        static value => new RepositoryId(value));

    /// <summary>
    /// <see cref="RepositoryAccess"/> wire-form converter — stores
    /// <see cref="RepositoryAccess.Value"/> (PascalCase) as a
    /// <c>varchar</c> so the existing partial indexes and queries
    /// match the historical <c>HasConversion&lt;string&gt;</c> shape.
    /// </summary>
    public static readonly ValueConverter<RepositoryAccess, string> RepositoryAccessToString = new(
        static access => access.Value,
        static value => RepositoryAccess.FromWire(value));
}
