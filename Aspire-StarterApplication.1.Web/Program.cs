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

var publicOriginValue = builder.Configuration["Authentication:PublicOrigin"];
if (publicOriginValue is null && builder.Configuration["CONTAINER_APP_HOSTNAME"] is { Length: > 0 })
{
    throw new InvalidOperationException("Authentication:PublicOrigin must be configured when hosted in Azure Container Apps.");
}

if (publicOriginValue is not null)
{
    if (!Uri.TryCreate(publicOriginValue, UriKind.Absolute, out var publicOrigin)
        || publicOrigin.Scheme != Uri.UriSchemeHttps || publicOrigin.AbsolutePath != "/"
        || publicOrigin.Query.Length > 0 || publicOrigin.Fragment.Length > 0 || publicOrigin.UserInfo.Length > 0)
    {
        throw new InvalidOperationException("Authentication:PublicOrigin must be an absolute HTTPS origin without a path, query, fragment, or credentials.");
    }

    // Use the same stable origin as the registration, not ACA's revision-specific hostname.
    app.Use((context, next) =>
    {
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(publicOrigin.Authority);
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
