namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Options for the file-based secret provider (issue #52, slice 1).
/// Bound from <c>[Secrets:File]</c> in TOML / <c>Secrets:File:*</c> in
/// appsettings. <see cref="Enabled"/> gates the provider's resolve
/// path — when false, <see cref="FileSecretProvider"/> short-circuits
/// to <c>null</c> before any filesystem access so a
/// <c>file:/...</c> reference surfaces a typed
/// <see cref="SecretRefUnsetException"/> rather than a successful
/// read of an unconfigured path. <see cref="RootPath"/>, when
/// set, constrains lookups to that base directory — paths outside the
/// root fail with <see cref="SecretRefFormatException"/> before any
/// filesystem read.
/// </summary>
public sealed class FileSecretOptions
{
    /// <summary>Configuration section name (<c>[Secrets:File]</c> in TOML / <c>Secrets:File:*</c> in appsettings).</summary>
    public const string SectionName = "Secrets:File";

    /// <summary>
    /// Master switch — when false, <see cref="FileSecretProvider"/>
    /// short-circuits to <c>null</c> (before any filesystem access) so
    /// a <c>file:/...</c> reference surfaces a typed
    /// <see cref="SecretRefUnsetException"/> rather than a successful
    /// read of an unconfigured path. The provider is registered
    /// unconditionally by the host; this option is the gate.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Optional allowlist root for <c>file:</c> references. When set, a
    /// reference whose <see cref="SecretRef.Path"/> is not under
    /// <see cref="RootPath"/> fails with
    /// <see cref="SecretRefFormatException"/> — a misconfigured
    /// reference cannot read arbitrary host files. Empty (default) =
    /// any path on the host.
    /// </summary>
    public string? RootPath { get; init; }
}
