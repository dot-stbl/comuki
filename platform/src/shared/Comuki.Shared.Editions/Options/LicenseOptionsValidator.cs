using Comuki.Shared.Kernel.Secrets;
using Microsoft.Extensions.Options;

namespace Comuki.Shared.Editions.Options;

/// <summary>
/// Cross-field validation for <see cref="LicenseOptions"/>: the
/// configured <see cref="LicenseOptions.Path"/>, when set, must resolve
/// to <em>something</em> via <see cref="ISecretResolver"/>; the grace
/// window and reload throttle must be in their declared ranges.
/// <para>
/// Deliberately does NOT check whether the resolved secret is a
/// cryptographically valid license — an unresolvable path is an
/// operator typo (env var or file genuinely missing) and fails boot
/// loudly ("present-but-broken fails loud"); a resolvable-but-broken
/// license (bad signature, wrong key, tampered content) is NOT a boot
/// failure — it degrades to Community at runtime with a logged
/// warning, and that behaviour lives in
/// <see cref="Edition.LicenseEdition"/>, not here.
/// </para>
/// <para>
/// Sync-over-async (<c>.GetAwaiter().GetResult()</c>) on
/// <see cref="ISecretResolver.ResolveAsync"/> is intentional and safe
/// here: <see cref="IValidateOptions{TOptions}"/> has no async overload,
/// and the .NET generic host runs startup validation without a
/// captured <see cref="SynchronizationContext"/>, so
/// the usual async-over-sync deadlock does not apply.
/// </para>
/// </summary>
/// <param name="secretResolver">Resolves <see cref="LicenseOptions.Path"/> at startup; injected so tests can NSubstitute it.</param>
public sealed class LicenseOptionsValidator(ISecretResolver secretResolver) : IValidateOptions<LicenseOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, LicenseOptions options)
    {
        var failures = new List<string>();

        if (options.GracePeriod < TimeSpan.Zero)
        {
            failures.Add("Host:License:GracePeriod cannot be negative.");
        }

        if (options.ReloadDelay <= TimeSpan.Zero)
        {
            failures.Add("Host:License:ReloadDelay must be positive.");
        }

        if (!string.IsNullOrWhiteSpace(options.Path))
        {
            try
            {
                // intentional sync-over-async at startup: IValidateOptions has
                // no async overload and the generic host runs validation without
                // a captured SynchronizationContext, so the usual deadlock does
                // not apply. Production code outside startup paths uses `await`.
#pragma warning disable VSTHRD002
                secretResolver.ResolveAsync(options.Path).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch (SecretRefUnsetException exception)
            {
                failures.Add($"Host:License:Path '{options.Path}' is set but could not be resolved: {exception.Message}");
            }
            catch (SecretRefFormatException exception)
            {
                failures.Add($"Host:License:Path '{options.Path}' is malformed: {exception.Message}");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
