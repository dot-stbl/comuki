using System.Globalization;
using Comuki.Modules.Knowledge.Infrastructure.Persistence;
using Comuki.Modules.Knowledge.Infrastructure.Persistence.Stores;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Unit coverage for <see cref="EmbeddingSql"/>: the wire-format
/// constants reference the schema/table by name (no magic strings), and
/// <see cref="EmbeddingSql.VectorLiteral"/> formats pgvector literals in
/// invariant culture — round-trippable through Postgres. The
/// availability probe and the cosine SELECT depend on the DB and live in
/// the integration suite.
/// </summary>
public sealed class EmbeddingSqlShould
{
    [Fact(DisplayName = "Given Dimensions const, when read, then it matches the OpenAI text-embedding-3-small default of 1536")]
    public void DimensionsDefaultIs1536()
    {
        EmbeddingSql.Dimensions.ShouldBe(1536);
    }

    [Fact(DisplayName = "Given EmbeddingColumnExistsSql, when read, then it queries the pgvector column under the knowledge schema")]
    public void EmbeddingColumnExistsSqlReferencesSchemaAndTable()
    {
        EmbeddingSql.EmbeddingColumnExistsSql.ShouldContain(KnowledgeDatabase.Schema);
        EmbeddingSql.EmbeddingColumnExistsSql.ShouldContain(KnowledgeDatabase.MemoryEmbeddings);
        EmbeddingSql.EmbeddingColumnExistsSql.ShouldContain("'embedding'");
    }

    [Fact(DisplayName = "Given CosineSearchSql, when read, then it filters by project + minSimilarity, orders by cosine distance, and limits to topK")]
    public void CosineSearchSqlCarriesFiltersAndOrder()
    {
        EmbeddingSql.CosineSearchSql.ShouldContain("embedding <=> @vector::vector");
        EmbeddingSql.CosineSearchSql.ShouldContain("ORDER BY embedding <=> @vector::vector");
        EmbeddingSql.CosineSearchSql.ShouldContain("LIMIT @limit");
        EmbeddingSql.CosineSearchSql.ShouldContain("@minSimilarity");
        EmbeddingSql.CosineSearchSql.ShouldContain("@projectId");
    }

    [Fact(DisplayName = "Given an empty vector, when VectorLiteral is called, then it returns \"[]\"")]
    public void VectorLiteralEmptyVector()
    {
        var literal = EmbeddingSql.VectorLiteral([]);

        literal.ShouldBe("[]");
    }

    [Fact(DisplayName = "Given a 3-component vector, when VectorLiteral is called, then it formats as a bracketed, comma-separated invariant string")]
    public void VectorLiteralFormatsThreeComponents()
    {
        var literal = EmbeddingSql.VectorLiteral([1.5f, -0.25f, 0f]);

        literal.ShouldBe("[1.5,-0.25,0]");
    }

    [Theory(DisplayName = "Given a vector with various components, when VectorLiteral is called, then the literal is invariant-culture and uses round-trip \"R\" format")]
    [InlineData(0.0001f)]
    [InlineData(1234567.89f)]
    [InlineData(-987654.321f)]
    public void VectorLiteralIsInvariantCulture(float component)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Force a comma-decimal culture; if VectorLiteral reads it,
            // the literal will contain "," between integer and fraction
            // and the test will fail.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var literal = EmbeddingSql.VectorLiteral([component]);

            // Invariant culture uses "." for the decimal separator;
            // de-DE would render it as ",". The presence of "." inside a
            // number is enough to detect a regression here.
            literal.ShouldContain(".");
            literal.ShouldNotContain(",");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact(DisplayName = "Given a vector formatted by VectorLiteral, when Postgres parses it back (simulated round-trip), then the components match")]
    public void VectorLiteralRoundTripsThroughParse()
    {
        var source = new[] { 0.1f, -0.2f, 0.3f, 4.5e6f, -7.890123f };

        var literal = EmbeddingSql.VectorLiteral(source);
        var withoutBrackets = literal[1..^1];
        var parsed = withoutBrackets.Split(',')
            .Select(static s => float.Parse(s, CultureInfo.InvariantCulture))
            .ToArray();

        parsed.Length.ShouldBe(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            parsed[index].ShouldBe(source[index]);
        }
    }
}
