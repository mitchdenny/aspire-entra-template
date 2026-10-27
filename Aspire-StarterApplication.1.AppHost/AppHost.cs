using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var env = builder.AddAzureContainerAppEnvironment("env");

var entra = builder.AddEntraIdApplication("entra-web")
    .WithIdTokenIssuance();

var apiService = builder.AddDotnetProject("apiservice", "../Aspire-StarterApplication.1.ApiService/Aspire-StarterApplication.1.ApiService.csproj")
    .DisableForwardedHeaders()
    .WithHttpHealthCheck("/health");

var web = builder.AddDotnetProject("webfrontend", "../Aspire-StarterApplication.1.Web/Aspire-StarterApplication.1.Web.csproj")
    // Redirect URIs are resolved while building the deploy pipeline, before environment inference.
    .WithComputeEnvironment(env)
    .DisableForwardedHeaders()
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(entra)
    .WithReference(apiService)
    .WaitFor(apiService)
    .PublishAsAzureContainerApp((_, app) =>
    {
        // A single replica avoids routing a Blazor circuit to another instance.
        app.Template.Scale.MinReplicas = 1;
        app.Template.Scale.MaxReplicas = 1;
    });

entra.WithRedirectUris(web.GetEndpoint("https"), "/signin-oidc", "/signout-callback-oidc");

if (builder.ExecutionContext.IsPublishMode)
{
#pragma warning disable ASPIRECOMPUTE002
    var host = env.Resource.GetHostAddressExpression(web.GetEndpoint("https"));
#pragma warning restore ASPIRECOMPUTE002
    web.WithEnvironment("Authentication__PublicOrigin", ReferenceExpression.Create($"https://{host}"));
}

builder.Build().Run();
