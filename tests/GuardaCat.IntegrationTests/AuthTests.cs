using System.Net;
using System.Net.Http.Json;
using GuardaCat.Contracts.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class AuthTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private const string Password = "Guarda.Test-2026!";

    [Fact]
    public async Task Registration_session_and_logout_enforce_cookie_and_csrf_contracts()
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        var account = NewAccount();
        using var registration = await PostAsync(client, "register", account);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var anonymous = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.True(anonymous.Headers.CacheControl?.NoStore);
        Assert.Null(anonymous.Headers.Location);

        var anonymousToken = await GetTokenAsync(client);
        using var login = await PostAsync(client, "login", Login(account), anonymousToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-GuardaCat.Session=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        using var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        var user = await me.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(account.DisplayName, user!.DisplayName);
        Assert.Equal(account.Email, user.Email);

        using var staleToken = await PostAsync(client, "logout", null, anonymousToken);
        Assert.Equal(HttpStatusCode.BadRequest, staleToken.StatusCode);
        Assert.Contains("csrf_invalid", await staleToken.Content.ReadAsStringAsync());
        using var logout = await PostAsync(client, "logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var loggedOut = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        using var relogin = await PostAsync(client, "login", Login(account));
        Assert.Equal(HttpStatusCode.NoContent, relogin.StatusCode);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    [InlineData("logout")]
    public async Task Mutations_without_csrf_are_rejected(string action)
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        using var response = await client.PostAsJsonAsync($"/api/auth/{action}", NewAccount());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("csrf_invalid", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData(" ", "valid@example.invalid", Password)]
    [InlineData("Valid name", "invalid-email", Password)]
    [InlineData("Valid name", "valid@example.invalid", "weak")]
    [InlineData("Valid name", "valid@example.invalid", "longbutweakpassword")]
    public async Task Registration_validates_on_the_server(string name, string email, string password)
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        using var response = await PostAsync(client, "register",
            new RegisterRequest { DisplayName = name, Email = email, Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Concurrent_registrations_cannot_duplicate_a_normalized_email()
    {
        await using var host = database.CreateHost();
        using var first = CreateClient(host);
        using var second = CreateClient(host);
        var account = NewAccount();
        var uppercase = new RegisterRequest
        {
            DisplayName = account.DisplayName, Email = $" {account.Email.ToUpperInvariant()} ", Password = Password
        };
        var responses = await Task.WhenAll(PostAsync(first, "register", account), PostAsync(second, "register", uppercase));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_keep_a_generic_error()
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        var account = NewAccount();
        using var registration = await PostAsync(client, "register", account);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await PostAsync(client, "login", new LoginRequest { Email = account.Email, Password = "Wrong-password1!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var locked = await PostAsync(client, "login", Login(account));
        using var unknown = await PostAsync(client, "login", Login(NewAccount()));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            (await locked.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Theory]
    [InlineData("login", 10)]
    [InlineData("register", 5)]
    public async Task Auth_routes_limit_requests_per_client(string action, int limit)
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        for (var attempt = 0; attempt < limit; attempt++)
        {
            using var response = await PostAsync(client, action, new LoginRequest());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var rejected = await PostAsync(client, action, new LoginRequest());
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task Another_host_accepts_the_cookie_using_persisted_keys()
    {
        await using var firstHost = database.CreateHost();
        using var first = CreateClient(firstHost);
        var account = NewAccount();
        using var registration = await PostAsync(first, "register", account);
        using var login = await PostAsync(first, "login", Login(account));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-GuardaCat.Session=", StringComparison.Ordinal)).Split(';')[0];

        await using var secondHost = database.CreateHost();
        using var second = CreateClient(secondHost);
        second.DefaultRequestHeaders.Add("Cookie", cookie);
        using var me = await second.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(account.Email, (await me.Content.ReadFromJsonAsync<CurrentUserResponse>())!.Email);
    }

    [Fact]
    public async Task Fresh_database_supports_readiness_and_host_serves_the_client()
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        using var ready = await client.GetAsync("/api/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("src=\"_framework/blazor.webassembly.js\"", html);
        Assert.DoesNotContain("#[.{fingerprint}]", html);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> host) => host.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Theory]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css", ".mud-")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.js", "mud")]
    public async Task Host_serves_component_library_assets(string path, string expectedContent)
    {
        await using var host = database.CreateHost();
        using var client = CreateClient(host);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(expectedContent, await response.Content.ReadAsStringAsync());
    }

    private static RegisterRequest NewAccount() => new()
    {
        DisplayName = "Prova Guarda", Email = $"test-{Guid.NewGuid():N}@example.invalid", Password = Password
    };

    private static LoginRequest Login(RegisterRequest account) => new() { Email = account.Email, Password = Password };

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<CsrfResponse>())!.Token;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object? body, string? token = null)
    {
        token ??= await GetTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{action}");
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}
