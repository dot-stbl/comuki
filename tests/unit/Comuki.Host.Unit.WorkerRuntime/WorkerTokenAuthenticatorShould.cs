using Comuki.Engine.Compute.Options;
using Comuki.Engine.Compute.Security;
using Comuki.Engine.Compute.Security.Stores;
using Comuki.Host.Workers;
using Comuki.Shared.Kernel.Ids;
using Shouldly;
using Xunit;

namespace Comuki.Host.Unit.WorkerRuntime;

/// <summary>
/// Unit tests for <see cref="WorkerTokenAuthenticator"/> over the real
/// <see cref="WorkerTokenIssuer"/> + in-memory store: issue/validate, Bearer
/// prefix tolerance and the revoke path.
/// </summary>
public sealed class WorkerTokenAuthenticatorShould
{
    private static (WorkerTokenIssuer Issuer, WorkerTokenAuthenticator Authenticator) CreatePair()
    {
        var issuer = new WorkerTokenIssuer(
            TimeProvider.System,
            new InMemoryWorkerTokenStore(),
            Microsoft.Extensions.Options.Options.Create(new WorkerTokenOptions { Pepper = "unit-test-pepper-16ch" }));
        return (issuer, new WorkerTokenAuthenticator(issuer));
    }

    [Fact(DisplayName = "Given an issued token, when authenticated, then it maps back to the worker")]
    public void MapIssuedTokenBackToWorker()
    {
        var (issuer, authenticator) = CreatePair();
        var workerId = WorkerId.New();
        var token = issuer.Issue(workerId);

        authenticator.Authenticate(token).ShouldBe(workerId);
    }

    [Fact(DisplayName = "Given a Bearer-prefixed token, when authenticated, then the prefix is tolerated")]
    public void TolerateBearerPrefix()
    {
        var (issuer, authenticator) = CreatePair();
        var token = issuer.Issue(WorkerId.New());

        authenticator.Authenticate($"Bearer {token}").ShouldNotBeNull();
    }

    [Fact(DisplayName = "Given garbage or no token, when authenticated, then the result is null")]
    public void RejectGarbageAndMissingTokens()
    {
        var (_, authenticator) = CreatePair();

        authenticator.Authenticate("garbage").ShouldBeNull();
        authenticator.Authenticate(null).ShouldBeNull();
        authenticator.Authenticate(string.Empty).ShouldBeNull();
    }

    [Fact(DisplayName = "Given a revoked worker token, when authenticated, then the result is null")]
    public void RejectRevokedToken()
    {
        var (issuer, authenticator) = CreatePair();
        var workerId = WorkerId.New();
        var token = issuer.Issue(workerId);
        issuer.Revoke(workerId);

        authenticator.Authenticate(token).ShouldBeNull();
    }
}
