using Microsoft.Extensions.Options;

namespace Comuki.Modules.Proxy.Application.Options;

/// <summary>
/// Cross-field validation for <see cref="ProxyOptions"/>:
/// when <see cref="ProxyOptions.Enabled"/> is <c>true</c>, at least one
/// <see cref="ProxyOptions.VirtualKeyConfiguration"/> is required and each
/// row's token / base URL / API-key env ref must be non-empty. The
/// DataAnnotation attributes on the type only cover per-field invariants;
/// the <see cref="Proxy:Enabled"/> → <see cref="Proxy:VirtualKeys"/>
/// relationship is here so the DataAnnotation pass doesn't trip on the
/// default empty <c>VirtualKeys</c> array when the proxy is disabled.
/// </summary>
public sealed class ProxyOptionsValidator : IValidateOptions<ProxyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProxyOptions options)
    {
        var failures = new List<string>();

        if (!options.Enabled)
        {
            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        if (options.VirtualKeys.Count == 0)
        {
            failures.Add("Proxy:VirtualKeys must contain at least one key when Proxy:Enabled is true");
        }

        for (var index = 0; index < options.VirtualKeys.Count; index++)
        {
            var key = options.VirtualKeys[index];
            var prefix = $"Proxy:VirtualKeys:{index}";

            if (string.IsNullOrWhiteSpace(key.Token))
            {
                failures.Add($"{prefix}:Token must not be empty");
            }

            if (key.ProjectId == Guid.Empty)
            {
                failures.Add($"{prefix}:ProjectId must be a non-empty GUID");
            }

            if (string.IsNullOrWhiteSpace(key.Provider))
            {
                failures.Add($"{prefix}:Provider must not be empty");
            }

            if (string.IsNullOrWhiteSpace(key.BaseUrl))
            {
                failures.Add($"{prefix}:BaseUrl must not be empty");
            }

            if (string.IsNullOrWhiteSpace(key.ApiKeyEnvRef))
            {
                failures.Add($"{prefix}:ApiKeyEnvRef must not be empty");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
