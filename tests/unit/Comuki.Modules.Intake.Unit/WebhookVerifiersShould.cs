using System.Security.Cryptography;
using System.Text;
using Comuki.Modules.Intake.Infrastructure.Providers.GitHub;
using Comuki.Modules.Intake.Infrastructure.Providers.GitLab;
using Comuki.Modules.Intake.Infrastructure.Providers.Jira;
using Comuki.Modules.Intake.Infrastructure.Providers.YandexTracker;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Intake.Unit;

/// <summary>
/// Webhook signature verifiers — happy path, tampered bodies, missing
/// secrets, malformed headers.
/// </summary>
public sealed class WebhookVerifiersShould
{
    private const string Secret = "whsec_very_secret_value";

    [Fact(DisplayName = "Given a correctly signed GitHub body, when verified, then it passes")]
    public void AcceptSignedGitHubBody()
    {
        var body = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"action":"opened"}""");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body)).ToLowerInvariant();

        GitHubWebhookVerifier.Verify(Secret, signature, body).ShouldBeTrue();
    }

    [Fact(DisplayName = "Given a tampered GitHub body, when verified, then it fails")]
    public void RefuseTamperedGitHubBody()
    {
        var body = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"action":"opened"}""");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body)).ToLowerInvariant();
        var tampered = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"action":"closed"}""");

        GitHubWebhookVerifier.Verify(Secret, signature, tampered).ShouldBeFalse();
    }

    [Theory(DisplayName = "Given a degenerate GitHub verification input, when verified, then it fails closed")]
    [InlineData(null, "sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("", "sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(Secret, null)]
    [InlineData(Secret, "md5=abc")]
    [InlineData(Secret, "sha256=tooshort")]
    [InlineData(Secret, "sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void FailClosedGitHub(string? secret, string? header)
    {
        GitHubWebhookVerifier.Verify(secret, header, "[]"u8).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a matching GitLab token, when verified, then it passes; a wrong one fails")]
    public void CompareGitLabToken()
    {
        GitLabWebhookVerifier.Verify(Secret, Secret).ShouldBeTrue();
        GitLabWebhookVerifier.Verify(Secret, "whsec_wrong").ShouldBeFalse();
        GitLabWebhookVerifier.Verify(null, Secret).ShouldBeFalse();
        GitLabWebhookVerifier.Verify(Secret, null).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a matching Yandex Tracker secret header, when verified, then it passes; a wrong one fails")]
    public void CompareYandexTrackerSecret()
    {
        YandexTrackerWebhookVerifier.Verify(Secret, Secret).ShouldBeTrue();
        YandexTrackerWebhookVerifier.Verify(Secret, "other").ShouldBeFalse();
        YandexTrackerWebhookVerifier.Verify(string.Empty, Secret).ShouldBeFalse();
    }

    [Fact(DisplayName = "Given a matching Jira secret param, when verified, then it passes; a wrong one fails")]
    public void CompareJiraSecret()
    {
        JiraWebhookVerifier.Verify(Secret, Secret).ShouldBeTrue();
        JiraWebhookVerifier.Verify(Secret, "nope").ShouldBeFalse();
        JiraWebhookVerifier.Verify(Secret, null).ShouldBeFalse();
    }
}
