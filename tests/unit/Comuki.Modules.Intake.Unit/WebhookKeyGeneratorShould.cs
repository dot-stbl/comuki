using Comuki.Modules.Intake.Application.Sources;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>Shape of generated webhook keys — length, alphabet, uniqueness.</summary>
public sealed class WebhookKeyGeneratorShould
{
    [Fact(DisplayName = "Given Generate, when called, then the key is 16 lowercase alphanumerics")]
    public void ProduceSixteenLowercaseAlphanumerics()
    {
        var key = WebhookKeyGenerator.Generate();

        key.Length.ShouldBe(16);
        key.ShouldMatch("^[a-z0-9]{16}$");
    }

    [Fact(DisplayName = "Given two Generate calls, when compared, then they differ")]
    public void ProduceDistinctKeys()
    {
        WebhookKeyGenerator.Generate().ShouldNotBe(WebhookKeyGenerator.Generate());
    }
}
