// Project-specific guard for the add-mission-cowork change (#105).
// Filter is a structural / range DSL; semantic search (FTS, vector recall) belongs
// to the retrieval layer (MemoryHybridRanking + MemoryFactSql.LexicalRankSql).
// If any filter operator silently sprouts a ts_rank / to_tsvector / EF.Functions
// path, this test fails — by name in the enum and by token in the production
// source.

using Comuki.Shared.Filtering.Ast;

using Shouldly;

using Xunit;

namespace Comuki.Shared.Filtering.Unit;

/// <summary>
///     Architectural guard: the Filter DSL is structural, not semantic. FTS and
///     vector recall live in the Memory retrieval layer; Filter must not absorb
///     them. Two layers of defence:
///     (1) the enum's names contain no FTS-shaped identifier;
///     (2) the production source of <c>Comuki.Shared.Filtering</c> does not
///         reference any FTS or vector SQL token.
/// </summary>
public sealed class FilterIsNotFtsGuardShould
{
    /// <summary>
    ///     Names that would suggest an FTS / vector / scoring operator has crept
    ///     into the Filter DSL. If a future operator is named anything in this
    ///     set, this test fails by name before the source scan even runs.
    /// </summary>
    private static readonly string[] forbiddenOperatorNames =
    [
        "Fts",
        "FullText",
        "Fulltext",
        "TextSearch",
        "Search",
        "Match",
        "Score",
        "Rank",
        "TsVector",
        "TsQuery",
        "Embedding",
        "Cosine",
    ];

    /// <summary>
    ///     Tokens that, if found in a Filter .cs file, indicate a leak from the
    ///     FTS / vector layer. Kept in this test (not in shared constants) on
    ///     purpose — the test owns the contract, the production code has no
    ///     reason to reference these names.
    /// </summary>
    private static readonly string[] forbiddenSourceTokens =
    [
        "ts_rank",
        "tsvector",
        "tsquery",
        "to_tsvector",
        "to_tsquery",
        "plainto_tsquery",
        "tsvector_ops",
        "EF.Functions",
        "tsmatch",
    ];

    [Fact(DisplayName = "Given the Filter operator enum, when scanned, then no name smells like FTS or vector recall")]
    public void FilterOperatorEnumHoldsNoFtsShapedValue()
    {
        var definedNames = Enum.GetNames<FilterOperator>();

        var offenders = definedNames
            .Where(name => forbiddenOperatorNames.Any(forbidden =>
                string.Equals(name, forbidden, StringComparison.Ordinal)))
            .ToArray();

        offenders.ShouldBeEmpty(
            "Filter is a structural DSL; FTS and vector recall belong in the Memory retrieval layer. "
            + "Offending names: " + string.Join(", ", offenders));
    }

    [Fact(DisplayName = "Given the production source of Comuki.Shared.Filtering, when scanned for FTS tokens, then none appear")]
    public void ProductionSourceContainsNoFtsTokens()
    {
        // Anchor on the solution file (comuki.slnx at the repo root) — the
        // assembly is loaded from the test's bin directory, so walking up
        // from there would land in `tests/`, not at the production source.
        var assemblyLocation = typeof(FilterOperator).Assembly.Location;
        var testBinDirectory = Path.GetDirectoryName(assemblyLocation)
            ?? throw new InvalidOperationException("Cannot resolve assembly directory.");

        var directory = new DirectoryInfo(testBinDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "comuki.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate comuki.slnx from " + testBinDirectory);

        var productionProjectRoot = Path.Combine(directory.FullName, "platform", "src", "shared", "Comuki.Shared.Filtering");
        Directory.Exists(productionProjectRoot).ShouldBeTrue(
            "Expected the production project to live at " + productionProjectRoot);

        var sourceFiles = new DirectoryInfo(productionProjectRoot)
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(static file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(static file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var offenders = new List<string>();
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file.FullName);
            foreach (var token in forbiddenSourceTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{file.Name}: '{token}'");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "FTS / vector SQL tokens are not part of the Filter DSL. "
            + "Offending locations: " + string.Join("; ", offenders));
    }
}
