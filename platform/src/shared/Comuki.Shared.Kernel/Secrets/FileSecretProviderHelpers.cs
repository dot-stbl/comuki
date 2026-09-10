namespace Comuki.Shared.Kernel.Secrets;

/// <summary>
/// Pure-logic helpers for <see cref="FileSecretProvider"/> — extracted
/// so the provider stays free of <c>private static</c> helpers
/// (per <c>class-layout-and-tooling.md</c> §1a — no private methods in
/// production). <c>internal</c> because only
/// <see cref="FileSecretProvider"/> in this assembly consumes the
/// helpers.
/// </summary>
internal static class FileSecretProviderHelpers
{
    /// <summary>
    /// Throw <see cref="SecretRefFormatException"/> when
    /// <paramref name="referencePath"/> resolves outside the
    /// <paramref name="root"/> allowlist. Both sides are normalized via
    /// <see cref="Path.GetFullPath(string)"/> so the prefix check is
    /// OS-aware (Linux <c>/</c> vs Windows <c>\</c> separators and
    /// relative-segment resolution). A trailing
    /// <see cref="Path.DirectorySeparatorChar"/> is appended to the
    /// canonical root so <c>/etc/comuki</c> does not match
    /// <c>/etc/comuki-other/pass</c>.
    /// </summary>
    /// <param name="referencePath">Raw path from the parsed <see cref="SecretRef"/>.</param>
    /// <param name="root">Configured <see cref="FileSecretOptions.RootPath"/>.</param>
    /// <exception cref="SecretRefFormatException">The reference path is outside <paramref name="root"/>.</exception>
    public static void EnforceRootPath(string referencePath, string root)
    {
        var fullReference = Path.GetFullPath(referencePath);
        var fullRoot = Path.GetFullPath(root);
        var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullReference.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new SecretRefFormatException(
                $"file reference '{referencePath}' is outside the configured RootPath allowlist '{root}'");
        }
    }
}
