namespace Comuki.TestFakeModel.Anthropic.Response;

/// <summary>
/// Splits a string into fixed-size, order-preserving substrings for
/// streaming delta events. Deterministic: the same text and chunk size
/// always yield the same chunk sequence, and concatenating the chunks
/// reproduces the input exactly.
/// </summary>
public static class TextChunker
{
    /// <summary>The default delta chunk size <c>AnthropicSseWriter</c> streams text and tool-input JSON with.</summary>
    public const int DefaultChunkSize = 24;

    /// <summary>Splits <paramref name="text"/> into chunks of at most <paramref name="chunkSize"/> characters. Empty input yields no chunks.</summary>
    public static IEnumerable<string> Chunk(string text, int chunkSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        for (var offset = 0; offset < text.Length; offset += chunkSize)
        {
            yield return text.Substring(offset, Math.Min(chunkSize, text.Length - offset));
        }
    }
}
