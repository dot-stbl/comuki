namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Splits an operator-facing reference string into a
/// <see cref="SecretRef"/>. Backward-compatible (bare names default to
/// the <c>env</c> scheme) and strict on what it accepts — unknown
/// schemes throw <see cref="SecretRefFormatException"/> at write time so
/// the call site fails fast on a typo, not at runtime when the first
/// request hits a missing provider. Internal so the composite resolver
/// (its only in-kernel consumer) can call into it; the public surface is
/// <see cref="ISecretResolver.ResolveAsync"/>.
/// </summary>
internal static class SecretRefParser
{
    /// <summary>Default scheme applied to bare references (no colon present).</summary>
    public const string DefaultScheme = "env";

    /// <summary>Reference → parsed <see cref="SecretRef"/>. Throws on unknown scheme.</summary>
    /// <param name="reference">Raw operator string (already trimmed).</param>
    public static SecretRef Parse(string reference)
    {
        var colonIndex = reference.IndexOf(':');
        if (colonIndex <= 0)
        {
            // Bare name: GH_TOKEN → env:GH_TOKEN. Keeps every pre-existing
            // SecretEnvRef row working unchanged (issue #52 §Slices 1).
            return new SecretRef(DefaultScheme, reference, null);
        }

        var scheme = reference[..colonIndex];
        var rest = reference[(colonIndex + 1)..];

        return scheme switch
        {
            "env" => new SecretRef("env", rest, null),
            "file" => new SecretRef("file", rest, null),
            // KV v2 path#key — slice 2/3 will register a Vault / Consul
            // provider and these branches start resolving. For now the
            // parser keeps the wire format stable so existing rows with a
            // vault/consul ref don't fail write-time validation.
            "vault" => SplitKvRef("vault", rest),
            "consul" => SplitKvRef("consul", rest),
            _ => throw new SecretRefFormatException(
                $"secret reference scheme '{scheme}' is not registered; "
                + "expected one of: env, file, vault, consul"),
        };
    }

    /// <summary>
    /// Splits <c>secret/prod/db#password</c> into
    /// <see cref="SecretRef.Path"/>=<c>secret/prod/db</c> and
    /// <see cref="SecretRef.Key"/>=<c>password</c>. Consul refs use the
    /// path as a single KV key — the <c>#</c> separator is a no-op for
    /// them but kept for parser symmetry so the wire format is
    /// provider-agnostic. File-scoped — the containing class is
    /// <c>file static class</c>, so the helper is reachable from
    /// <see cref="Parse"/> only and from no other file in the assembly.
    /// </summary>
    public static SecretRef SplitKvRef(string scheme, string rest)
    {
        var hashIndex = rest.IndexOf('#');
        return hashIndex < 0
            ? new SecretRef(scheme, rest, null)
            : new SecretRef(scheme, rest[..hashIndex], rest[(hashIndex + 1)..]);
    }
}
