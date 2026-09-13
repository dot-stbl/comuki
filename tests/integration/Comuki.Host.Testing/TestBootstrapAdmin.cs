using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Comuki.Host.Testing;

/// <summary>The bootstrap admin account every host-booting integration suite configures through <c>auth:bootstrap:*</c>.</summary>
public static class TestBootstrapAdmin
{
    public const string Email = "bootstrap@comuki.test";
    public const string Password = "bootstrap-pass-1";

    /// <summary>Binds the bootstrap admin's email/password onto the builder's configuration.</summary>
    public static void Configure(IConfiguration configuration)
    {
        configuration["auth:bootstrap:adminEmail"] = Email;
        configuration["auth:bootstrap:adminPassword"] = Password;
    }
}

/// <summary>Login helper for cookie-carrying <see cref="HttpClient"/>s against the bootstrap admin.</summary>
public static class HttpClientBootstrapAdminExtensions
{
    /// <summary>Logs the client in as the bootstrap admin over the real login endpoint and returns it, so construction and login chain in one expression.</summary>
    public static async Task<HttpClient> LoginAsBootstrapAdminAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = TestBootstrapAdmin.Email, password = TestBootstrapAdmin.Password },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return client;
    }
}
