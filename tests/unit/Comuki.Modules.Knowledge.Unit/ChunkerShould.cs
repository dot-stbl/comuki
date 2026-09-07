using Comuki.Modules.Knowledge.Infrastructure.Chunking;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Knowledge.Unit;

/// <summary>
/// Paragraph-aware text chunker contract: empty input is a no-op, target
/// caps are enforced as soft limits, paragraphs are never split mid-sentence
/// (an oversized paragraph becomes its own chunk), and <c>EstimateTokens</c>
/// reports <c>ceil(words / 0.75)</c> with whitespace tolerance.
/// </summary>
public sealed class ChunkerShould
{
    [Theory(DisplayName = "Given empty or whitespace input, when Chunk is called, then it returns an empty list")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    [InlineData("\t\r\n")]
    public void ReturnEmptyListForEmptyOrWhitespaceInput(string text)
    {
        var chunks = Chunker.Chunk(text, targetTokens: 100);

        chunks.ShouldBeEmpty();
    }

    [Theory(DisplayName = "Given a non-positive target tokens, when Chunk is called, then it throws ArgumentOutOfRangeException")]
    [InlineData(0)]
    [InlineData(-1)]
    public void ThrowOnNonPositiveTargetTokens(int targetTokens)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Chunker.Chunk("anything", targetTokens));
    }

    [Fact(DisplayName = "Given text smaller than target, when Chunk is called, then the whole text is one chunk")]
    public void ReturnSingleChunkForTextUnderTarget()
    {
        var chunks = Chunker.Chunk("hello world", targetTokens: 100);

        chunks.ShouldHaveSingleItem().ShouldBe("hello world");
    }

    [Fact(DisplayName = "Given three paragraphs under target, when Chunk is called, then they are packed into one chunk separated by \\n\\n")]
    public void PackMultipleParagraphsIntoSingleChunkUnderTarget()
    {
        var chunks = Chunker.Chunk("p1\n\np2\n\np3", targetTokens: 100);

        chunks.ShouldHaveSingleItem().ShouldBe("p1\n\np2\n\np3");
    }

    [Fact(DisplayName = "Given a single paragraph longer than target, when Chunk is called, then it becomes its own chunk and is not split")]
    public void KeepOversizedParagraphAsItsOwnChunk()
    {
        var paragraph = "this paragraph contains many words to inflate the token count well above target";

        var chunks = Chunker.Chunk(paragraph, targetTokens: 10);

        chunks.ShouldHaveSingleItem().ShouldBe(paragraph);
    }

    [Fact(DisplayName = "Given text with mixed paragraphs, when Chunk is called, then chunks respect target tokens and never split mid-paragraph")]
    public void RespectTargetTokensAndNeverSplitMidParagraph()
    {
        // 5-word paragraphs each estimate to 7 tokens under the 0.75 heuristic;
        // two fit inside a 20-token cap, adding a third would cross it and forces a flush.
        var p1 = "alpha beta gamma delta epsilon";
        var p2 = "zeta eta theta iota kappa";
        var p3 = "mu nu xi omicron pi";
        var p4 = "rho sigma tau upsilon phi";

        var chunks = Chunker.Chunk($"{p1}\n\n{p2}\n\n{p3}\n\n{p4}", targetTokens: 20);

        chunks.Count.ShouldBe(2);
        chunks[0].ShouldBe($"{p1}\n\n{p2}");
        chunks[1].ShouldBe($"{p3}\n\n{p4}");
    }

    [Theory(DisplayName = "Given N whitespace-separated words, when EstimateTokens is called, then it returns ceil(N / 0.75)")]
    [InlineData("", 0)]
    [InlineData("hello", 2)]              // N=1 → ceil(1.333) = 2
    [InlineData("hello world", 3)]        // N=2 → ceil(2.666) = 3
    [InlineData("one two three", 4)]      // N=3 → ceil(4.000) = 4
    [InlineData("a b c d e f g", 10)]     // N=7 → ceil(9.333) = 10
    [InlineData("  hello   world  ", 3)]  // whitespace-tolerant: still 2 words
    public void ReturnCeilWordsDividedByPointSevenFive(string text, int expected)
    {
        var estimate = Chunker.EstimateTokens(text);

        estimate.ShouldBe(expected);
    }
}
