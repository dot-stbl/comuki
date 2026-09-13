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
    /// object-axis query filter at all — currently empty: all ten
    /// DbContexts in the solution declare at least one. Add an entry here
    /// (with the reason in a comment) rather than silently letting a new
    /// DbContext ship with no scoping story.
    /// </summary>
    private static readonly HashSet<Type> exemptFromQueryFilter = [];

    /// <summary>Every DbContext type in the solution, named explicitly (see class remarks on why this can't be a pure assembly scan).</summary>
    private static readonly Type[] allDbContextTypes =
    [
        typeof(Engine.Orchestration.Infrastructure.Persistence.OrchestrationDbContext),
        typeof(Modules.Artifacts.Infrastructure.Persistence.ArtifactsDbContext),
        typeof(Modules.Chat.Infrastructure.Persistence.ChatDbContext),
        typeof(Modules.Costs.Infrastructure.Persistence.CostsDbContext),
        typeof(Modules.Identity.Infrastructure.Persistence.IdentityDbContext),
        typeof(Modules.Intake.Infrastructure.Persistence.IntakeDbContext),
        typeof(Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext),
        typeof(Modules.Memory.Infrastructure.Persistence.MemoryDbContext),
        typeof(Modules.Projects.Infrastructure.Persistence.ProjectsDbContext),
        typeof(Modules.Scheduler.Infrastructure.Persistence.SchedulerDbContext),
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
    /// excludes compiler-generated/<c>file</c>-scoped helper types (names
    /// starting with <c>&lt;</c>) — a <c>file</c> class is only reachable
    /// through the public type in the same source file that uses it
    /// (here, <c>EfMemoryStore</c>'s private <c>MemoryFactVectors</c>
    /// helper, which reads scope off the <c>MemoryDbContext</c> it is
    /// handed rather than depending on the accessor directly), so the
    /// public type is where this rule's guarantee actually has to hold.
    /// </summary>
    [Fact]
    public void RawAdoConsumersOfPgvectorSchemasAlsoDependOnSubjectScopeAccessor()
    {
        var knowledge = CheckAssembly(typeof(Modules.Knowledge.Infrastructure.Persistence.KnowledgeDbContext).Assembly);
        var memory = CheckAssembly(typeof(Modules.Memory.Infrastructure.Persistence.MemoryDbContext).Assembly);

        Assert.True(knowledge.IsSuccessful, Failing(knowledge));
        Assert.True(memory.IsSuccessful, Failing(memory));

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

        static string Failing(NetArchTest.Rules.TestResult result)
        {
            return "Raw-ADO type(s) with no ISubjectScopeAccessor dependency: "
                + string.Join(", ", result.FailingTypeNames ?? []);
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
