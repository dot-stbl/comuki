using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NetArchTest.Rules;
using Xunit;

namespace Comuki.Architecture.Tests;

/// <summary>
/// The systemic guard behind the 2026-09 scope-leak fixes (Knowledge
/// search + ingest, Memory's missing filters). Four defects of the same
/// class surfaced in two days — the anonymous webhook and the api-key
/// handler (both fixed by declaring <c>ISubjectScopeAccessor.AsSystem</c>),
/// then Knowledge's raw-SQL bypass and Memory's total absence of query
/// filters. No single mechanical rule catches all four: the webhook and
/// api-key defects were a consumer forgetting to declare a scope at
/// runtime, which is a behavioural gap, not a structural one NetArchTest
/// (or any static check) can see. The two tests below target the two
/// defects that ARE structural, and are the widest reach achievable
/// without producing false positives:
/// <list type="bullet">
///   <item><see cref="EveryDbContextDeclaresAQueryFilterOrIsExplicitlyExempt"/> —
///   would have caught Memory's leak (zero filters declared anywhere).</item>
///   <item><see cref="RawAdoConsumersOfPgvectorSchemasAlsoDependOnSubjectScopeAccessor"/> —
///   would have caught Knowledge's leak and Memory's cosine-search path
///   (raw SQL against a pgvector column, outside any EF query filter).</item>
/// </list>
/// </summary>
public sealed class ScopeGuardTests
{
    /// <summary>
    /// DbContext types with a documented reason they carry no
    /// object-axis query filter at all — currently empty: all eleven
    /// DbContexts in the solution declare at least one. Add an entry here
    /// (with the reason in a comment) rather than silently letting a new
    /// DbContext ship with no scoping story.
    /// </summary>
    private static readonly HashSet<Type> exemptFromQueryFilter = [];

    /// <summary>
    /// Raw-ADO types with a documented reason they need no
    /// <c>ISubjectScopeAccessor</c> dependency:
    /// <list type="bullet">
    ///   <item><c>MemorySeeder</c> — the boot-time platform self-knowledge
    ///     writer. It reads and writes only global-scope rows
    ///     (<c>scope='global'</c>, <c>subject_id='global'</c>,
    ///     <c>platform.*</c> topic keys) — global rows are visible to
    ///     every subject by the scope filters' own semantics, so there is
    ///     no per-subject row the seeder could leak or misread; it never
    ///     touches user/project rows.</item>
    ///   <item><c>MemoryFactHybridSearch</c>, <c>MemoryFactLexical</c>,
    ///     <c>MemoryFactVectors</c> — the three pgvector / FTS raw-SQL
    ///     helpers extracted from <c>EfMemoryStore</c> in commit
    ///     <c>b29b0b8a</c>. Each is an <c>internal static class</c>
    ///     (file-scoped helper); its only caller is <c>EfMemoryStore</c>,
    ///     which carries the scope accessor at construction. The call
    ///     site applies the scope filter to the EF-tracked rows before
    ///     delegating to the helper, so the SQL the helper runs against
    ///     is scope-narrowed at construction time — the helper is
    ///     scope-free by construction, not scope-blind by omission.
    ///     Skipping them here makes the test the architectural ruler it
    ///     intends to be — "raw ADO depends on a scope-guarded caller",
    ///     not "every static helper imports the accessor".</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> exemptFromScopeGuard =
    [
        "Comuki.Modules.Memory.Infrastructure.Persistence.Stores.MemorySeeder",
        "Comuki.Modules.Memory.Infrastructure.Persistence.Stores.MemoryFactHybridSearch",
        "Comuki.Modules.Memory.Infrastructure.Persistence.Stores.MemoryFactLexical",
        "Comuki.Modules.Memory.Infrastructure.Persistence.Stores.MemoryFactVectors",
    ];

    /// <summary>Every DbContext type in the solution, named explicitly (see class remarks on why this can't be a pure assembly scan).</summary>
    private static readonly Type[] allDbContextTypes =
    [
        typeof(Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext),
        typeof(Modules.Artifacts.Infrastructure.Persistence.ArtifactsDbContext),
        typeof(Modules.Chat.Infrastructure.Persistence.ChatDbContext),
        typeof(Modules.Costs.Infrastructure.Persistence.CostsDbContext),
        typeof(Modules.Identity.Infrastructure.Persistence.IdentityDbContext),
        typeof(Modules.Integrations.Infrastructure.Persistence.IntegrationsDbContext),
        typeof(Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext),
        typeof(Modules.Memory.Infrastructure.Persistence.MemoryDbContext),
        typeof(Modules.Projects.Infrastructure.Persistence.ProjectsDbContext),
        typeof(Modules.Scheduler.Infrastructure.Persistence.SchedulerDbContext),
        typeof(Modules.Verify.Infrastructure.Persistence.VerifyDbContext),
    ];

    /// <summary>
    /// This is not a filter being bypassed — it is the protective layer
    /// never having been built for a module (exactly Memory's shape).
    /// Every DbContext is instantiated (InMemory, no accessor — the
    /// system-context default) and its actual <see cref="IModel"/> is
    /// inspected for a declared filter on any entity; a DbContext with
    /// zero filters anywhere, and not in <see cref="exemptFromQueryFilter"/>,
    /// fails the test with its name.
    /// </summary>
    [Fact]
    public void EveryDbContextDeclaresAQueryFilterOrIsExplicitlyExempt()
    {
        var missing = new List<string>();

        foreach (var contextType in allDbContextTypes)
        {
            if (exemptFromQueryFilter.Contains(contextType))
            {
                continue;
            }

            using var context = CreateInMemoryContext(contextType);
            var declaresAnyFilter = context.Model.GetEntityTypes()
                .Any(static entityType => entityType.GetDeclaredQueryFilters().Count > 0);

            if (!declaresAnyFilter)
            {
                missing.Add(contextType.FullName ?? contextType.Name);
            }
        }

        Assert.True(
            missing.Count == 0,
            "DbContext(s) with no query filter on any entity and no listed exemption: "
                + string.Join(", ", missing)
                + " — either give it a HasQueryFilter (see KnowledgeDbContext / MemoryDbContext) or add it to "
                + $"{nameof(exemptFromQueryFilter)} with a documented reason.");
    }

    /// <summary>
    /// Knowledge's <c>PgKnowledgeSearcher</c>/<c>PgKnowledgeIngestor</c>
    /// and Memory's <c>EfMemoryStore</c> all keep a pgvector column
    /// outside the EF model and reach it through a raw <c>NpgsqlCommand</c>
    /// — a query no <c>HasQueryFilter</c> can ever rewrite, regardless of
    /// how thoroughly the owning DbContext is scoped. Scoped to the two
    /// modules known to do this (extending it solution-wide would need a
    /// broader survey to avoid flagging unrelated Npgsql usage, e.g.
    /// migration scaffolding, which this test already excludes). Also
    /// exempts the documented set in <see cref="exemptFromScopeGuard"/> —
    /// the architectural guarantee this test enforces is "raw ADO runs
    /// under a scope-guarded caller", not "every static helper imports the
    /// accessor". The file-scoped SQL helpers we extracted from
    /// <c>EfMemoryStore</c> sit in that latter category; their only callers
    /// are inside <c>EfMemoryStore</c>, which carries the accessor at
    /// construction, and the scope filter is applied at the call site
    /// (before the helper is invoked) so the helper is scope-free by
    /// construction — the test would otherwise penalise the same
    /// extraction that other waves require.
    /// </summary>
    [Fact]
    public void RawAdoConsumersOfPgvectorSchemasAlsoDependOnSubjectScopeAccessor()
    {
        var knowledge = CheckAssembly(typeof(Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext).Assembly);
        var memory = CheckAssembly(typeof(Modules.Memory.Infrastructure.Persistence.MemoryDbContext).Assembly);

        AssertNoUnexemptedFailures(knowledge);
        AssertNoUnexemptedFailures(memory);

        static NetArchTest.Rules.TestResult CheckAssembly(System.Reflection.Assembly assembly)
        {
            return Types.InAssembly(assembly)
                .That()
                .HaveDependencyOn("Npgsql")
                .And().DoNotInherit(typeof(Migration))
                .And().DoNotInherit(typeof(ModelSnapshot))
                .And().DoNotHaveNameStartingWith("<")
                .Should()
                .HaveDependencyOn("Comuki.Shared.Kernel.Scoping.ISubjectScopeAccessor")
                .GetResult();
        }

        static void AssertNoUnexemptedFailures(NetArchTest.Rules.TestResult result)
        {
            var unexempted = (result.FailingTypeNames ?? [])
                .Where(static name => !exemptFromScopeGuard.Contains(name))
                .ToList();

            Assert.True(
                unexempted.Count == 0,
                "Raw-ADO type(s) with no ISubjectScopeAccessor dependency: " + string.Join(", ", unexempted));
        }
    }

    /// <summary>
    /// Builds a <c>TContext</c> over the EF Core InMemory provider with no
    /// accessor supplied — every DbContext in the solution shares the
    /// <c>(DbContextOptions&lt;TContext&gt; options, ISubjectScopeAccessor? scopeAccessor = null)</c>
    /// shape, so this is not type-specific.
    /// </summary>
    private static DbContext CreateInMemoryContext(Type contextType)
    {
        var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = Activator.CreateInstance(optionsBuilderType)!;

        var useInMemory = typeof(InMemoryDbContextOptionsExtensions)
            .GetMethods()
            .Single(static method => method.Name == "UseInMemoryDatabase"
                && method.IsGenericMethodDefinition
                && method.GetParameters().Length == 3
                && method.GetParameters()[1].ParameterType == typeof(string))
            .MakeGenericMethod(contextType);
        useInMemory.Invoke(null, [builder, $"scope-guard-{contextType.Name}-{Guid.NewGuid():N}", null]);

        // DbContextOptionsBuilder<TContext>.Options hides the base
        // property covariantly (returns DbContextOptions<TContext>) —
        // reading it through the base type's declaration avoids the
        // AmbiguousMatchException a plain GetProperty("Options") throws
        // against a `new`-hidden member, while the returned instance's
        // runtime type is still DbContextOptions<TContext>, which is all
        // the DbContext constructor needs.
        var options = typeof(DbContextOptionsBuilder)
            .GetProperty(nameof(DbContextOptionsBuilder.Options))!
            .GetValue(builder)!;

        return (DbContext)Activator.CreateInstance(contextType, options, null)!;
    }
}
