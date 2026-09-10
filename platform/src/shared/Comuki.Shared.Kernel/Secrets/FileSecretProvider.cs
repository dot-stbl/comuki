using Microsoft.Extensions.Options;

namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Resolves a parsed reference against the filesystem — reads the file
/// at <see cref="SecretRef.Path"/> and trims trailing whitespace (mounted
/// Kubernetes secrets ship with a trailing newline). Returns <c>null</c>
/// when the file is absent — the resolver wraps the null in
/// <see cref="SecretRefUnsetException"/>. When
/// <see cref="FileSecretOptions.Enabled"/> is false the provider
/// short-circuits to <c>null</c> before any filesystem access (the
/// host registers it unconditionally; the gate is the option, not the
/// DI registration — issue #52 slice-2 audit M1/M6). Singleton:
/// stateless, I/O happens at call time, not at startup. The
/// <see cref="FileSecretOptions.RootPath"/> allowlist (when set)
/// constrains lookups to a base directory so a misconfigured reference
/// cannot read arbitrary host files — paths outside the root fail with
/// <see cref="SecretRefFormatException"/> before any filesystem read.
/// The allowlist check lives in <see cref="FileSecretProviderHelpers"/>
/// (separate class per <c>code-shape.md</c> §1a — no private methods in
/// production).
/// </summary>
public sealed class FileSecretProvider(IOptions<FileSecretOptions> options) : ISecretProvider
{
    /// <inheritdoc />
    public string Scheme => "file";

    /// <summary>Resolves by reading and trimming the file at <see cref="SecretRef.Path"/>. Short-circuits to <c>null</c> when <see cref="FileSecretOptions.Enabled"/> is false.</summary>
    /// <param name="reference">Parsed reference — <see cref="SecretRef.Path"/> is the file path.</param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="SecretRefFormatException">The ref path is outside <see cref="FileSecretOptions.RootPath"/> (when set).</exception>
    public Task<string?> ResolveAsync(SecretRef reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!options.Value.Enabled)
        {
            return Task.FromResult<string?>(null);
        }

        var root = options.Value.RootPath;
        if (!string.IsNullOrWhiteSpace(root))
        {
            FileSecretProviderHelpers.EnforceRootPath(reference.Path, root);
        }

        if (!File.Exists(reference.Path))
        {
            return Task.FromResult<string?>(null);
        }

        var content = File.ReadAllText(reference.Path).TrimEnd();
        return Task.FromResult<string?>(content);
    }
}
