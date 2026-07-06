using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Microsoft.Extensions.Options;

namespace Comuki.Platform.Routing.Services;

/// <inheritdoc />
public sealed class QuotaExhaustionDetector(IOptions<RotationOptions> options) : IQuotaExhaustionDetector
{
    public bool IsExhausted(int statusCode, string? body)
    {
        foreach (var rule in options.Value.ExhaustionRules)
        {
            var statusMatches = rule.StatusCode is null || rule.StatusCode.Value == statusCode;
            var bodyMatches = string.IsNullOrEmpty(rule.BodyContains)
                || (body is not null && body.Contains(rule.BodyContains, StringComparison.OrdinalIgnoreCase));

            if (statusMatches && bodyMatches)
            {
                return true;
            }
        }

        return false;
    }
}
