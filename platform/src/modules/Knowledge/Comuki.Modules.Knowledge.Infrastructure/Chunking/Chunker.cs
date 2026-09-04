namespace Comuki.Modules.Knowledge.Infrastructure.Chunking;

/// <summary>
/// Fixed-size paragraph-aware text chunker. Splits on double-newline
/// boundaries (paragraph) and packs consecutive paragraphs into a
/// chunk until the running estimated-token count crosses
/// <see cref="TargetTokens"/>; an over-sized paragraph (longer than
/// the target on its own) becomes its own chunk — never split mid-word.
/// The token estimate is <c>whitespace-separated word count ÷ 0.75</c>;
/// deliberately cheap (no tokenizer dependency) and over-estimates by
/// 20–30 % vs OpenAI's BPE, which keeps real chunks inside the model's
/// context window without over-running the embedder quota.
/// </summary>
public static class Chunker
{
    /// <summary>Heuristic — English BPE density ≈ 0.75 tokens per whitespace-separated word.</summary>
    public const double TokensPerWord = 0.75;

    /// <summary>Splits <paramref name="text"/> into chunks of roughly <paramref name="targetTokens"/> tokens.</summary>
    /// <param name="text">Input text — paragraphs separated by blank lines.</param>
    /// <param name="targetTokens">Soft cap per chunk (estimated tokens).</param>
    /// <returns>Non-empty ordered chunk list. An empty or whitespace input yields an empty list.</returns>
    public static IReadOnlyList<string> Chunk(string text, int targetTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (targetTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTokens), targetTokens, "target tokens must be positive");
        }

        var paragraphs = text
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();
        var currentTokens = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphTokens = EstimateTokens(paragraph);

            // An over-long paragraph becomes its own chunk — we never
            // split a paragraph mid-sentence, the embedder gets the full
            // context and downstream retrieval is the better for it.
            if (paragraphTokens > targetTokens)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                    currentTokens = 0;
                }

                chunks.Add(paragraph);
                continue;
            }

            if (currentTokens + paragraphTokens > targetTokens && current.Length > 0)
            {
                chunks.Add(current.ToString());
                current.Clear();
                currentTokens = 0;
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
            currentTokens += paragraphTokens;
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        return chunks;
    }

    /// <summary>Token estimate for one paragraph — used by the caller to populate <c>token_count</c> in the row.</summary>
    /// <param name="text"></param>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var words = 0;
        var inWord = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (inWord)
                {
                    words++;
                    inWord = false;
                }
            }
            else
            {
                inWord = true;
            }
        }

        if (inWord)
        {
            words++;
        }

        return (int)Math.Ceiling(words / TokensPerWord);
    }
}
