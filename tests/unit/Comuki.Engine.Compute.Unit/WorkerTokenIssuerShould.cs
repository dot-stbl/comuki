using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Engine.Compute.Unit;

/// <summary>
/// Unit tests for <see cref="WorkerTokenIssuer"/>: issue→validate roundtrip,
/// tampered/unknown tokens, expiry via the fake clock, revoke and re-issue.
/// </summary>
public sealed class WorkerTokenIssuerShould
{
    private readonly FakeTimeProvider clock = new();
    private readonly InMemoryWorkerTokenStore store = new();

    private WorkerTokenIssuer CreateIssuer()
    {
        return new(clock, store, Microsoft.Extensions.Options.Options.Create(new WorkerTokenOptions()));
    }

    [Fact(DisplayName = "When issue Then Validate Returns Worker Id, then test passes")]
    public void IssueThenValidateReturnsWorkerId()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();

        var token = issuer.Issue(workerId);

        // 256-bit → 32 bytes → 43 unpadded base64url characters, URL-safe alphabet
        token.Length.ShouldBe(43);
        token.ShouldMatch("^[A-Za-z0-9_-]+$");
        issuer.Validate(token).ShouldBe(workerId);
    }

    [Fact(DisplayName = "When issue With Explicit Ttl Overriding Default, then test passes")]
    public void IssueWithExplicitTtlOverridingDefault()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();

        var token = issuer.Issue(workerId, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(4));
        issuer.Validate(token).ShouldBe(workerId);
    }

    [Fact(DisplayName = "When reject Unknown Token, then test passes")]
    public void RejectUnknownToken()
    {
        var issuer = CreateIssuer();

        issuer.Issue(WorkerId.New());

        issuer.Validate("not-a-issued-token").ShouldBeNull();
    }

    [Fact(DisplayName = "When reject Expired Token, then test passes")]
    public void RejectExpiredToken()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();
        var token = issuer.Issue(workerId, TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(6));

        issuer.Validate(token).ShouldBeNull();
    }

    [Fact(DisplayName = "When reject Tampered Token, then test passes")]
    public void RejectTamperedToken()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();

        var token = issuer.Issue(workerId);
        var tampered = token[..^1] + (token[^1] == 'A' ? "B" : "A");

        issuer.Validate(tampered).ShouldBeNull();
    }

    [Fact(DisplayName = "When reject Revoked Token, then test passes")]
    public void RejectRevokedToken()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();
        var token = issuer.Issue(workerId);

        issuer.Revoke(workerId);

        issuer.Validate(token).ShouldBeNull();
    }

    [Fact(DisplayName = "When replace Previous Token On Reissue, then test passes")]
    public void ReplacePreviousTokenOnReissue()
    {
        var workerId = WorkerId.New();
        var issuer = CreateIssuer();
        var firstToken = issuer.Issue(workerId);

        var secondToken = issuer.Issue(workerId);

        secondToken.ShouldNotBe(firstToken);
        issuer.Validate(firstToken).ShouldBeNull();
        issuer.Validate(secondToken).ShouldBe(workerId);
    }

    [Fact(DisplayName = "When validate Tokens Of Distinct Workers Independently, then test passes")]
    public void ValidateTokensOfDistinctWorkersIndependently()
    {
        var firstWorkerId = WorkerId.New();
        var secondWorkerId = WorkerId.New();
        var issuer = CreateIssuer();

        var firstToken = issuer.Issue(firstWorkerId);
        var secondToken = issuer.Issue(secondWorkerId);

        issuer.Validate(firstToken).ShouldBe(firstWorkerId);
        issuer.Validate(secondToken).ShouldBe(secondWorkerId);
    }
}
