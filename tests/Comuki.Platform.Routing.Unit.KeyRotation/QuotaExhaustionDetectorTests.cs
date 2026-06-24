using Comuki.Platform.Routing.Interfaces;
using Comuki.Platform.Routing.Options;
using Comuki.Platform.Routing.Services;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Comuki.Platform.Routing.Unit.KeyRotation;

public sealed class QuotaExhaustionDetectorTests
{
    private static IQuotaExhaustionDetector CreateSut()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RotationOptions
        {
            ApiKeys = ["k1"],
            UpstreamUrl = "https://upstream.example",
            ExhaustionRules =
            [
                new ExhaustionRule { StatusCode = 429 },
                new ExhaustionRule { BodyContains = "insufficient balance" },
                new ExhaustionRule { StatusCode = 401, BodyContains = "quota" },
            ],
        });
        return new QuotaExhaustionDetector(options);
    }

    [Theory]
    [InlineData(429, "rate limited", true)]            // правило по статусу
    [InlineData(400, "insufficient balance now", true)] // правило по телу
    [InlineData(401, "quota exceeded", true)]           // правило статус+тело
    [InlineData(401, "bad api key", false)]             // 401 без "quota" — не наше
    [InlineData(500, "internal error", false)]          // обычная ошибка апстрима
    [InlineData(200, "", false)]                        // успех
    public void IsExhausted_MatchesConfiguredRules(int status, string body, bool expected)
    {
        var sut = CreateSut();

        sut.IsExhausted(status, body).ShouldBe(expected);
    }

    [Fact]
    public void IsExhausted_BodyMatch_IsCaseInsensitive()
    {
        var sut = CreateSut();

        sut.IsExhausted(400, "INSUFFICIENT BALANCE").ShouldBeTrue();
    }
}
