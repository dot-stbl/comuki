using Comuki.Engine.Orchestration.Domain;
using Comuki.Engine.Orchestration.Domain.Runs;
using Comuki.Engine.Orchestration.Infrastructure.Persistence;
using Comuki.Shared.Filtering.Parser;
using Comuki.Shared.Filtering.Ports;
using Comuki.Shared.Kernel.Ids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace Comuki.Shared.Filtering.Integration.Ef;

/// <summary>
/// One smoke suite over the ported filter DSL against real PostgreSQL: the
/// EF translator must turn every operator shape into SQL the Npgsql provider
/// accepts (in-memory LINQ never proves translatability). Runs through the
/// orchestration <see cref="Run"/> aggregate — the first consumer of the DSL
/// (GET /api/v1/runs) — covering filter (eq/in/range/contains/logic/parens/
/// now()), sort, and the FilterParseException paths.
/// </summary>
public sealed class RunsFilteringShould : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    /// <summary>
    /// boundary: initialised in InitializeAsync before any test runs
    /// </summary>
    private OrchestrationDbContext db = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await container.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<OrchestrationDbContext>();
        OrchestrationDbContext.ApplyOptions(options, container.GetConnectionString());
        db = new OrchestrationDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        // Direct construction = system consumer: the scope filter is inactive
        // and the DSL sees the raw table. Statuses/dates chosen so every filter
        // below has a discriminating answer.
        var seed = new[]
        {
            (RunStatus.Queued, now.AddHours(-1)),
            (RunStatus.Running, now.AddHours(-2)),
            (RunStatus.Succeeded, now.AddDays(-3)),
            (RunStatus.Failed, now.AddDays(-8)),
            (RunStatus.Cancelled, now.AddDays(-30)),
        };

        foreach (var (status, createdAt) in seed)
        {
            var run = Run.Create(ProjectId.New(), createdAt);

            // legal hops only (RunTransitions): Succeeded needs Running first.
            if (status is RunStatus.Succeeded)
            {
                run.TransitionTo(RunStatus.Running, createdAt.AddMinutes(1));
            }

            if (status is not RunStatus.Queued)
            {
                run.TransitionTo(status, createdAt.AddMinutes(5));
            }

            _ = db.Runs.Add(run);
        }

        _ = await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await db.DisposeAsync();
        await container.DisposeAsync();
    }

    private async Task<List<string>> SelectStatusesAsync(string? filter)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return await db.Runs.AsNoTracking()
            .ApplyFilter(filter)
            .OrderBy(static run => run.CreatedAt)
            .Select(static run => run.Status.ToString())
            .ToListAsync(cancellationToken);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered by status eq, then only that status returns")]
    public async Task FilterByStatusEqualityAsync()
    {
        var statuses = await SelectStatusesAsync("Status==Running");

        statuses.ShouldBe(["Running"]);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered by status in-list, then every listed status returns")]
    public async Task FilterByStatusInListAsync()
    {
        var statuses = await SelectStatusesAsync("Status[]=Succeeded,Failed");

        // ordered by CreatedAt asc: failed (-8d) before succeeded (-3d)
        statuses.ShouldBe(["Failed", "Succeeded"]);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered by createdAt range, then only runs inside the window return")]
    public async Task FilterByCreatedAtRangeAsync()
    {
        var statuses = await SelectStatusesAsync(
            $"CreatedAt>={DateTimeOffset.UtcNow.AddDays(-4):O};CreatedAt<{DateTimeOffset.UtcNow.AddDays(-1):O}");

        statuses.ShouldBe(["Succeeded"]);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered with now(-7d), then recent runs return")]
    public async Task FilterByNowFunctionAsync()
    {
        var statuses = await SelectStatusesAsync("CreatedAt>=now(-7d)");

        // failed (-8d), cancelled (-30d) drop out
        statuses.ShouldBe(["Succeeded", "Running", "Queued"]);
    }

    [Fact(DisplayName = "Given seeded runs, when filtered with parens and or, then precedence holds")]
    public async Task FilterWithParensAndOrAsync()
    {
        var statuses = await SelectStatusesAsync("(Status==Queued|Status==Running);CreatedAt>now(-90m)");

        // the paren group picks queued(-1h)+running(-2h); the AND half drops the -2h run
        statuses.ShouldBe(["Queued"]);
    }

    [Fact(DisplayName = "Given seeded runs, when sorted by status asc, then SQL orders by the enum value")]
    public async Task SortByStatusAscAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var statuses = await db.Runs.AsNoTracking()
            .ApplySort("Status,asc")
            .Select(static run => run.Status.ToString())
            .ToListAsync(cancellationToken);

        // the runs schema stores the status enum as text — SQL orders lexically
        statuses.ShouldBe(["Cancelled", "Failed", "Queued", "Running", "Succeeded"]);
    }

    [Fact(DisplayName = "Given seeded runs, when sorted by createdAt desc, then newest run comes first")]
    public async Task SortByCreatedAtDescAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var statuses = await db.Runs.AsNoTracking()
            .ApplySort("CreatedAt,desc")
            .Select(static run => run.Status.ToString())
            .ToListAsync(cancellationToken);

        statuses.ShouldBe(["Queued", "Running", "Succeeded", "Failed", "Cancelled"]);
    }

    [Fact(DisplayName = "Given the DSL, when an unknown field is named, then FilterParseException surfaces")]
    public async Task RejectUnknownFieldAsync()
    {
        await Should.ThrowAsync<FilterParseException>(() => SelectStatusesAsync("Nope==1"));
    }

    [Fact(DisplayName = "Given the DSL, when an operator the field disallows is used, then FilterParseException surfaces")]
    public async Task RejectDisallowedOperatorAsync()
    {
        // Status is an enum: no contains
        await Should.ThrowAsync<FilterParseException>(() => SelectStatusesAsync("Status~Run"));
    }

    [Fact(DisplayName = "Given the DSL, when a value cannot convert, then FilterParseException surfaces")]
    public async Task RejectUnconvertibleValueAsync()
    {
        await Should.ThrowAsync<FilterParseException>(() => SelectStatusesAsync("Status==NotAStatus"));
    }
}
