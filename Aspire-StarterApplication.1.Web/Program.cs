using Aspire_StarterApplication._1.Web;
using Aspire_StarterApplication._1.Web.Components;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.GetSection("AzureAd").Bind(options);
        // Sign-in only: validate an ID token, without redeeming a code or requesting API tokens.
        options.ResponseType = OpenIdConnectResponseType.IdToken;
        options.ResponseMode = OpenIdConnectResponseMode.FormPost;
    });
builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
    options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Keep ASP.NET Core's default loopback-only proxy trust; never trust arbitrary forwarded hosts.
});

builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new("https+http://apiservice");
});

var app = builder.Build();

app.UseForwardedHeaders();

if (builder.Configuration["CONTAINER_APP_HOSTNAME"] is { Length: > 0 } containerAppHostname)
{
    // ACA terminates TLS. Its platform-provided hostname is the registered public origin,
    // independent of untrusted Host/X-Forwarded-* headers or the ingress proxy's changing IP.
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(containerAppHostname);
        return next(context);
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/authentication/signin", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }))
    .AllowAnonymous();

app.MapPost("/authentication/signout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        return Results.BadRequest();
    }

    return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
}).RequireAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
