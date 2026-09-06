using Comuki.Modules.Identity.Application.ApiKeys;
using Comuki.Modules.Identity.Application.Options;
using Comuki.Modules.Identity.Application.Ports;
using Comuki.Modules.Identity.Domain.ApiKeys;
using Comuki.Modules.Identity.Domain.Ids;
using Comuki.Shared.Kernel.Ids;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Comuki.Modules.Identity.Unit;

/// <summary>
/// Issue path (T4.3): the issuer generates a parsable token, persists a
/// row whose HMAC verifies the plaintext (the same constant-time compare
/// the auth handler runs), and never stores the secret itself.
/// </summary>
public sealed class ApiKeyIssuerShould
{
    private readonly IApiKeyStore apiKeyStore = Substitute.For<IApiKeyStore>();
    private readonly ApiKeyHasher hasher = new(Options.Create(new ApiKeyOptions
    {
        Pepper = "unit-test-pepper-0123456789abcdef",
    }));
    private readonly FakeTime clock = new();

    [Fact(DisplayName = "Given a user, when a key is issued, then the stored HMAC verifies the returned plaintext")]
    public async Task IssueKeyWhoseHmacVerifiesThePlaintextAsync()
    {
        var userId = UserId.New();
        var issuer = new ApiKeyIssuer(apiKeyStore, hasher, clock);

        var credential = await issuer.IssueAsync(userId, "ci-key", null, TestContext.Current.CancellationToken);

        var stored = apiKeyStore.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<ApiKey>();
        stored.UserId.ShouldBe(userId);
        stored.Prefix.ShouldBe(credential.Prefix);
        stored.IsActive.ShouldBeTrue();
        stored.TenantProjectId.ShouldBeNull();
        hasher.Verify(credential.PlaintextToken, stored.KeyHmac).ShouldBeTrue();
        stored.KeyHmac.ShouldNotContain(credential.PlaintextToken);
        credential.Prefix.Length.ShouldBe(ApiKeyToken.PrefixLength);
    }

    [Fact(DisplayName = "Given an issued token, when parsed, then the prefix matches the stored row")]
    public async Task IssueTokenWithConsistentPrefixAsync()
    {
        var issuer = new ApiKeyIssuer(apiKeyStore, hasher, clock);

        var credential = await issuer.IssueAsync(UserId.New(), "ci-key", null, TestContext.Current.CancellationToken);

        var parsed = ApiKeyToken.Parse(credential.PlaintextToken).ShouldNotBeNull();
        parsed.Prefix.ShouldBe(credential.Prefix);
    }

    [Fact(DisplayName = "Given a tenant project id, when a scoped key is issued, then the row carries it")]
    public async Task IssueScopedKeyPersistsTenantProjectIdAsync()
    {
        var tenantId = ProjectId.New();
        var issuer = new ApiKeyIssuer(apiKeyStore, hasher, clock);

        var credential = await issuer.IssueAsync(UserId.New(), "fleet", tenantId, TestContext.Current.CancellationToken);

        var stored = apiKeyStore.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<ApiKey>();
        stored.TenantProjectId.ShouldBe(tenantId);
        credential.TenantProjectId.ShouldBe(tenantId.Value);
    }

    private sealed class FakeTime : TimeProvider;
}
