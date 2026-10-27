using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Aspire_StarterApplication._1.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Aspire_StarterApplication._1.Tests;

#if (TestFx == "MSTest")
[TestClass]
#endif
public class WebTests
{
#if (TestFx == "xUnit.net" && XUnitVersion != "v2")
    private static CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;
#elif (TestFx == "NUnit")
    private static CancellationToken TestCancellationToken => TestContext.CurrentContext.CancellationToken;
#else
    private static CancellationToken TestCancellationToken => CancellationToken.None;
#endif

#if (TestFx == "MSTest")
    [TestMethod]
#elif (TestFx == "NUnit")
    [Test]
#elif (TestFx == "xUnit.net")
    [Fact]
#endif
    public async Task AnonymousPagesChallengeWithAnIdTokenOnlyFlow()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = CreateClient(factory);

        foreach (var path in new[] { "/", "/counter", "/weather", "/authentication/signin" })
        {
            using var response = await client.GetAsync(path, TestCancellationToken);
            AssertStatus(HttpStatusCode.Redirect, response);
            var location = response.Headers.Location!.AbsoluteUri;
            AssertContains("https://login.microsoftonline.com/", location);
            AssertContains("response_type=id_token", location);
            AssertContains("response_mode=form_post", location);
            AssertContains("redirect_uri=https%3A%2F%2Flocalhost%2Fsignin-oidc", location);
        }

        using var health = await client.GetAsync("/health", TestCancellationToken);
        AssertStatus(HttpStatusCode.OK, health);
        using var icon = await client.GetAsync("/favicon.png", TestCancellationToken);
        AssertStatus(HttpStatusCode.OK, icon);
        using var circuit = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null, TestCancellationToken);
        AssertStatus(HttpStatusCode.Redirect, circuit);
    }

#if (TestFx == "MSTest")
    [TestMethod]
#elif (TestFx == "NUnit")
    [Test]
#elif (TestFx == "xUnit.net")
    [Fact]
#endif
    public async Task SignedInPagesRenderAndWeatherCallsTheInternalApi()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = CreateClient(factory, signedIn: true);

        foreach (var path in new[] { "/", "/counter", "/weather" })
        {
            using var response = await client.GetAsync(path, TestCancellationToken);
            AssertStatus(HttpStatusCode.OK, response);
            var html = await response.Content.ReadAsStringAsync(TestCancellationToken);
            AssertContains("Test user", html);
            AssertContains("/authentication/signout", html);
            if (path == "/weather")
            {
                AssertContains("Test forecast", html);
            }
        }
    }

#if (TestFx == "MSTest")
    [TestMethod]
#elif (TestFx == "NUnit")
    [Test]
#elif (TestFx == "xUnit.net")
    [Fact]
#endif
    public async Task LogoutRequiresAnAntiforgeryToken()
    {
        await using var factory = new AuthWebApplicationFactory();
        using var client = CreateClient(factory, signedIn: true);

        using var invalid = await client.PostAsync("/authentication/signout", new FormUrlEncodedContent([]), TestCancellationToken);
        AssertStatus(HttpStatusCode.BadRequest, invalid);

        var html = await client.GetStringAsync("/", TestCancellationToken);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var valid = await client.PostAsync("/authentication/signout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), TestCancellationToken);
        AssertStatus(HttpStatusCode.Redirect, valid);
        AssertContains("/oauth2/v2.0/logout", valid.Headers.Location!.AbsoluteUri);
        AssertContains("signout-callback-oidc", valid.Headers.Location.AbsoluteUri);

        using var getLogout = await client.GetAsync("/authentication/signout", TestCancellationToken);
        AssertStatus(HttpStatusCode.NotFound, getLogout);
    }

#if (TestFx == "MSTest")
    [TestMethod]
#elif (TestFx == "NUnit")
    [Test]
#elif (TestFx == "xUnit.net")
    [Fact]
#endif
    public async Task ContainerAppsUsesItsTrustedHttpsOriginForCallbacks()
    {
        await using var factory = new AuthWebApplicationFactory(containerApps: true);
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Host = "untrusted.example";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "untrusted.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        using var response = await client.GetAsync("/", TestCancellationToken);
        AssertStatus(HttpStatusCode.Redirect, response);
        AssertContains("redirect_uri=https%3A%2F%2Fwebfrontend.example.azurecontainerapps.io%2Fsignin-oidc",
            response.Headers.Location!.AbsoluteUri);

        using var signedInClient = CreateClient(factory, signedIn: true);
        signedInClient.DefaultRequestHeaders.Host = "untrusted.example";
        var html = await signedInClient.GetStringAsync("/", TestCancellationToken);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var signout = await signedInClient.PostAsync("/authentication/signout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }), TestCancellationToken);
        AssertStatus(HttpStatusCode.Redirect, signout);
        AssertContains("post_logout_redirect_uri=https%3A%2F%2Fwebfrontend.example.azurecontainerapps.io%2Fsignout-callback-oidc",
            signout.Headers.Location!.AbsoluteUri);
    }

#if (TestFx == "MSTest")
    [TestMethod]
#elif (TestFx == "NUnit")
    [Test]
#elif (TestFx == "xUnit.net")
    [Fact]
#endif
    public void ContainerAppsRejectsMissingOrInvalidPublicOrigins()
    {
        foreach (var origin in new string?[]
        {
            null, "", "http://web.example", "/relative", "https://web.example/path",
            "https://web.example?query=value", "https://web.example/#fragment", "https://user@web.example"
        })
        {
            using var factory = new AuthWebApplicationFactory(containerApps: true, publicOrigin: origin);
            try
            {
                using var client = CreateClient(factory);
                throw new Exception("An invalid public origin was accepted.");
            }
            catch (InvalidOperationException exception)
            {
                AssertContains("Authentication:PublicOrigin", exception.Message);
            }
        }
    }

    private static HttpClient CreateClient(AuthWebApplicationFactory factory, bool signedIn = false)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", "true");
        }
        return client;
    }

    private static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response)
    {
#if (TestFx == "MSTest")
        Assert.AreEqual(expected, response.StatusCode);
#elif (TestFx == "NUnit")
        Assert.That(response.StatusCode, Is.EqualTo(expected));
#elif (TestFx == "xUnit.net")
        Assert.Equal(expected, response.StatusCode);
#endif
    }

    private static void AssertContains(string expected, string actual)
    {
#if (TestFx == "MSTest")
        StringAssert.Contains(actual, expected);
#elif (TestFx == "NUnit")
        Assert.That(actual, Does.Contain(expected));
#elif (TestFx == "xUnit.net")
        Assert.Contains(expected, actual);
#endif
    }
}

internal sealed class AuthWebApplicationFactory(bool containerApps = false,
    string? publicOrigin = "https://webfrontend.example.azurecontainerapps.io") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(containerApps ? "Production" : "Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
            ["AzureAd:TenantId"] = "11111111-1111-1111-1111-111111111111",
            ["AzureAd:ClientId"] = "22222222-2222-2222-2222-222222222222",
            ["CONTAINER_APP_HOSTNAME"] = containerApps ? "webfrontend--0000002.example.azurecontainerapps.io" : null,
            ["Authentication:PublicOrigin"] = containerApps ? publicOrigin : null
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options => options.DefaultAuthenticateScheme = "Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            // Static discovery and test-only authentication avoid Azure access or cloud provisioning.
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/oauth2/v2.0/authorize",
                    EndSessionEndpoint = "https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/oauth2/v2.0/logout"
                };
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
            });
            services.AddHttpClient<WeatherApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new TestWeatherHandler());
        });
    }
}

internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers["X-Test-User"] != "true")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test user")], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

internal sealed class TestWeatherHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new WeatherForecast(new DateOnly(2026, 1, 1), 20, "Test forecast") })
        });
}
