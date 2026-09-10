namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Resolves a parsed reference against the process environment. The
/// reference's <see cref="SecretRef.Path"/> is the env-var name (bare
/// references default to the <c>env</c> scheme, see
/// <see cref="SecretRefParser"/>). Returns <c>null</c> when the var is
/// unset — the resolver wraps the null in
/// <see cref="SecretRefUnsetException"/> for callers that need a hard
/// error. Singleton: stateless, thread-safe.
/// </summary>
public sealed class EnvSecretProvider : ISecretProvider
{
    /// <inheritdoc />
    public string Scheme => "env";

    /// <inheritdoc />
    public Task<string?> ResolveAsync(SecretRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = Environment.GetEnvironmentVariable(reference.Path);
        return Task.FromResult(value);
    }
}
