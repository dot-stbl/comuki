using System.Globalization;
using Comuki.Modules.Memory.Infrastructure.Persistence.Stores;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Memory.Unit;

/// <summary>
/// pgvector literal formatting: invariant round-trippable numbers in
/// bracket form, regardless of the current culture.
/// </summary>
public sealed class MemoryFactSqlShould
{
    [Fact(DisplayName = "Given a vector, when formatted as a literal, then it is bracketed invariant text")]
    public void FormatVectorLiteralInvariantly()
    {
        var vector = new float[] { 1f, 0.5f, -0.25f, 0 };

        var literal = MemoryFactSql.VectorLiteral(vector);

        literal.ShouldBe("[1,0.5,-0.25,0]");
    }

    [Fact(DisplayName = "Given a culture with a comma decimal separator, when a vector is formatted, then the literal stays dot-decimal")]
    public void KeepDotDecimalUnderCommaCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("ru-RU");
        try
        {
            var literal = MemoryFactSql.VectorLiteral([0.5f]);

            literal.ShouldBe("[0.5]");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
