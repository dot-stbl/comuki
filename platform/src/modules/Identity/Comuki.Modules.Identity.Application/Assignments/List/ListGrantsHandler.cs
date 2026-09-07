using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Application.Views;

namespace Comuki.Modules.Identity.Application.Assignments.List;

/// <summary>
/// Reads a page of role assignments (active + revoked) for the
/// identity-admin list (<c>GET /api/v1/grants</c>, issue #45 / F13). The
/// store returns a page sorted by creation time (newest first) plus the
/// unfiltered total; the handler projects the domain rows to the read
/// view the dashboard consumes.
/// </summary>
/// <param name="assignments">Persistence port (scoped — DbContext per request).</param>
public sealed class ListGrantsHandler(IRoleAssignmentStore assignments)
{
    /// <summary>Reads the requested page.</summary>
    /// <param name="query"></param>
    /// <param name="cancellationToken"></param>
    public async Task<(IReadOnlyList<RoleAssignmentView> Items, int Total)> HandleAsync(
        ListGrantsQuery query,
        CancellationToken cancellationToken = default)
    {
        var skip = (query.Page - 1) * query.PageSize;
        var (rows, total) = await assignments.ListAsync(
            query.SubjectKind,
            query.SubjectId,
            skip,
            query.PageSize,
            cancellationToken);

        var items = new RoleAssignmentView[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            items[index] = AccountMapper.ToView(rows[index]);
        }

        return (items, total);
    }
}
