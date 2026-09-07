using Comuki.Modules.Intake.Application.Sources;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Generated secret shape — entropy, encoding, uniqueness (issue #46).
/// </summary>
public sealed class WebhookSecretGeneratorShould
{
    [Fact(DisplayName = "Given Generate, when called, then the secret is 64 lowercase hex chars")]
    public void ProduceSixtyFourLowercaseHexChars()
    {
        var secret = WebhookSecretGenerator.Generate();

        secret.Length.ShouldBe(64);
        secret.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Theory(DisplayName = "Given two Generate calls, when compared, then they differ")]
    [InlineData(8)]
    [InlineData(64)]
    public void ProduceDistinctSecrets(int sampleSize)
    {
        var samples = Enumerable.Range(0, sampleSize)
            .Select(static _ => WebhookSecretGenerator.Generate())
            .ToArray();

        samples.Distinct(StringComparer.Ordinal).Count().ShouldBe(sampleSize);
    }
}
