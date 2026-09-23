using System.Security.Cryptography;
using System.Text;
using Comuki.TestFakeModel.Cassettes.Wire;

namespace Comuki.TestFakeModel.Cassettes.Matching;

/// <summary>
/// Builds a <see cref="CassetteRequestMatch"/> from an <see cref="ObservedRequest"/>
/// — design.md: <c>matchOn</c> never stores the raw prompt, only a SHA-256
/// hash of it plus the tool-result structural predicate.
/// </summary>
public static class CassetteMatchKeyBuilder
{
    /// <summary>Builds the match key <paramref name="observed"/> would record/replay against.</summary>
    public static CassetteRequestMatch Build(ObservedRequest observed)
    {
        return new CassetteRequestMatch(HashLastUserMessage(observed.LastUserMessageText), observed.HasToolResult);
    }

    /// <summary>Hashes <paramref name="text"/> as <c>sha256:&lt;hex&gt;</c>, or <c>null</c> when there's no last user message to hash.</summary>
    public static string? HashLastUserMessage(string? text)
    {
        return text is null ? null : $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}";
    }
}
