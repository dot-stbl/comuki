using System.ComponentModel.DataAnnotations;
using Comuki.Modules.Observability.Application.Options;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Observability.Unit;

/// <summary>
/// Unit tests for the typed <see cref="ObservabilityOptions"/>: the
/// <c>[Range]</c> clamp on <see cref="ObservabilityOptions.ScrapeInterval"/>
/// + the cross-field <see cref="ObservabilityOptions.Validate"/> rule
/// that an override, if set, must be an http(s) URL. Both rules
/// participate in the host's <c>ValidateOnStart()</c> — the contract
/// here is "the typed validator catches a bad host before the first
/// /api/v1/mcp dispatch".
/// </summary>
public sealed class ObservabilityOptionsShould
{
    /// <summary>The defaults are the spec-mandated values, not whatever the operator leaves empty.</summary>
    [Fact(DisplayName = "Given a fresh options instance, when read, then defaults match the spec")]
    public void DefaultValuesMatchSpec()
    {
        var options = new ObservabilityOptions();

        ObservabilityOptions.SectionName.ShouldBe("Observability:Victoria");
        options.ScrapeInterval.ShouldBe(TimeSpan.FromSeconds(15));
        options.RetentionPeriod.ShouldBeNull();
        options.LogsBaseUrl.ShouldBeNull();
        options.MetricsBaseUrl.ShouldBeNull();
    }

    /// <summary>5s is the spec-mandated lower bound on ScrapeInterval.</summary>
    [Fact(DisplayName = "Given ScrapeInterval = 5s, when validated, then no error")]
    public void ScrapeIntervalFiveSecondsIsValid()
    {
        var options = new ObservabilityOptions { ScrapeInterval = TimeSpan.FromSeconds(5) };
        var results = Validate(options);

        results.ShouldBeEmpty();
    }

    /// <summary>5m is the spec-mandated upper bound on ScrapeInterval.</summary>
    [Fact(DisplayName = "Given ScrapeInterval = 5m, when validated, then no error")]
    public void ScrapeIntervalFiveMinutesIsValid()
    {
        var options = new ObservabilityOptions { ScrapeInterval = TimeSpan.FromMinutes(5) };
        var results = Validate(options);

        results.ShouldBeEmpty();
    }

    /// <summary>Below 5s the typed Range rule rejects the value.</summary>
    [Fact(DisplayName = "Given ScrapeInterval < 5s, when validated, then error cites the range")]
    public void ScrapeIntervalBelowRangeIsRejected()
    {
        var options = new ObservabilityOptions { ScrapeInterval = TimeSpan.FromSeconds(4) };
        var results = Validate(options);

        results.Count.ShouldBe(1);
        results[0].ErrorMessage!.ShouldContain("5s..5m");
    }

    /// <summary>Above 5m the typed Range rule rejects the value.</summary>
    [Fact(DisplayName = "Given ScrapeInterval > 5m, when validated, then error cites the range")]
    public void ScrapeIntervalAboveRangeIsRejected()
    {
        var options = new ObservabilityOptions { ScrapeInterval = TimeSpan.FromMinutes(6) };
        var results = Validate(options);

        results.Count.ShouldBe(1);
        results[0].ErrorMessage!.ShouldContain("5s..5m");
    }

    /// <summary>The cross-field validator rejects a non-http(s) scheme. We use
    /// <c>javascript:</c> because the <c>[Url]</c> attribute accepts
    /// it (so the cross-field rule is what flags the URI, not the
    /// attribute itself — that's the contract under test).</summary>
    [Fact(DisplayName = "Given LogsBaseUrl = javascript: scheme, when validated, then error cites the scheme")]
    public void LogsBaseUrlMustBeHttpOrHttps()
    {
        var options = new ObservabilityOptions { LogsBaseUrl = new Uri("javascript:alert(1)") };
        var results = Validate(options);

        results.ShouldNotBeEmpty();
        results.ShouldContain(static result =>
            result.MemberNames.Contains(nameof(ObservabilityOptions.LogsBaseUrl)) &&
            result.ErrorMessage!.Contains("http(s)"));
    }

    /// <summary>Same rule on the metrics override — symmetric guard.</summary>
    [Fact(DisplayName = "Given MetricsBaseUrl = javascript: scheme, when validated, then error cites the scheme")]
    public void MetricsBaseUrlMustBeHttpOrHttps()
    {
        var options = new ObservabilityOptions { MetricsBaseUrl = new Uri("javascript:alert(1)") };
        var results = Validate(options);

        results.ShouldNotBeEmpty();
        results.ShouldContain(static result =>
            result.MemberNames.Contains(nameof(ObservabilityOptions.MetricsBaseUrl)) &&
            result.ErrorMessage!.Contains("http(s)"));
    }

    /// <summary>
    /// Drive the typed validator: <see cref="ObservabilityOptions"/> is
    /// <see cref="IValidatableObject"/>, so <c>Validate(...)</c> collects
    /// every <c>ValidationResult</c> (Range + cross-field) in one pass.
    /// </summary>
    private static List<ValidationResult> Validate(ObservabilityOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        foreach (var result in options.Validate(context))
        {
            results.Add(result);
        }
        return results;
    }
}
