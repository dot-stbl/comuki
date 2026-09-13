using Comuki.Modules.Knowledge.Application.Documents;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>Page-bounds normalization of the documents listing.</summary>
public sealed class KnowledgeDocumentsPagingShould
{
    [Theory(DisplayName = "Given page bounds, when normalized, then page ≥ 1 and size clamps to [1, 100]")]
    [InlineData(0, 25, 1, 25)]
    [InlineData(-3, 25, 1, 25)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(2, 500, 2, 100)]
    [InlineData(4, 10, 4, 10)]
    public void NormalizeBounds(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var (normalizedPage, normalizedSize) = KnowledgeDocumentsPaging.Normalize(page, pageSize);

        normalizedPage.ShouldBe(expectedPage);
        normalizedSize.ShouldBe(expectedPageSize);
    }
}
