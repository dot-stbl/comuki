using System.Text.RegularExpressions;

namespace Comuki.TestFakeModel.Cassettes.Redaction;

/// <summary>
/// Secret-shaped string values and denylisted field names — design.md's
/// "Cassette format and redaction": <c>sk-…</c>, <c>ck_…</c>, <c>Bearer …</c>,
/// the minted virtual-key pattern, or a name on the redaction denylist
/// (<c>ANTHROPIC_AUTH_TOKEN</c>, <c>COMUKI_WORKER_TOKEN</c>, any
/// <c>Secrets:*</c> resolved value). Patterns mirror the global
/// <c>~/.agents/rules/secrets.md</c> recognized-secret table (defense in
/// depth against a stray token leaking into an otherwise-allowlisted
/// response field, e.g. a tool_use argument).
/// </summary>
public static partial class SecretPatterns
{
    /// <summary>True when <paramref name="value"/> matches a known secret shape.</summary>
    public static bool LooksLikeSecret(string value)
    {
        return ApiKeyPattern().IsMatch(value)
            || VirtualKeyPattern().IsMatch(value)
            || BearerTokenPattern().IsMatch(value)
            || GitHubTokenPattern().IsMatch(value)
            || AwsAccessKeyPattern().IsMatch(value)
            || DenylistedEnvNamePattern().IsMatch(value);
    }

    /// <summary>True when <paramref name="fieldName"/> is itself a denylisted secret name — its value is redacted outright, never merely disallowed.</summary>
    public static bool IsDenylistedFieldName(string fieldName)
    {
        return fieldName is "ANTHROPIC_AUTH_TOKEN" or "COMUKI_WORKER_TOKEN"
            || fieldName.StartsWith("Secrets:", StringComparison.Ordinal);
    }

    // sk-… (OpenAI) / sk-ant-… (Anthropic).
    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{10,}")]
    private static partial Regex ApiKeyPattern();

    // ck_… — comuki virtual-key prefix (Proxy-minted keys).
    [GeneratedRegex(@"ck_[A-Za-z0-9_-]{6,}")]
    private static partial Regex VirtualKeyPattern();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    // GitHub PAT — defense in depth, unlikely in a model response but cheap to catch.
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{20,}")]
    private static partial Regex GitHubTokenPattern();

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}")]
    private static partial Regex AwsAccessKeyPattern();

    // A denylisted env-var name immediately followed by its resolved value (KEY=value or KEY: value).
    [GeneratedRegex(@"(ANTHROPIC_AUTH_TOKEN|COMUKI_WORKER_TOKEN)\s*[:=]\s*\S+")]
    private static partial Regex DenylistedEnvNamePattern();
}
