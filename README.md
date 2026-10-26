# Aspire Entra starter

The Aspire Blazor starter, with Microsoft Entra ID sign-in and an Azure Container Apps
deployment model. The frontend is authenticated; its weather API is internal to the
Container Apps environment. No client secret or delegated API tokens are required.

**Preview:** this uses the experimental Aspire 17 daily Entra integration, not the stable
NuGet release. Tested with Aspire CLI `17.0.0-preview.1.26526.2` and .NET 10.
All Aspire hosting packages follow the installed CLI's matching daily version.

## Prerequisites

- .NET 10 SDK and the [daily Aspire CLI](https://aspire.dev/get-started/install-cli/).
  A stable CLI cannot supply the unreleased Entra integration. Check `aspire --version`.
- Azure CLI, signed in with `az login`, and Bicep **0.36.1 or later**
  (`az bicep version`; upgrade with `az bicep upgrade` if needed).
- An Azure subscription in the Entra tenant you want to use. Your account needs resource
  group deployment permissions and permission to create Entra app registrations and
  service principals. Deployment also needs permission to assign managed-identity roles.
  Tenant policies can require administrator assistance.
- Access to the daily NuGet feeds and `mcr.microsoft.com` (the pinned Microsoft Graph
  Bicep extension is downloaded during compilation).
- A supported container runtime for building/pushing deployment images. The default local
  app has no Redis/database container dependency.

## Create and run

```bash
aspire new \
  --template-repository https://github.com/mitchdenny/aspire-entra-template \
  --name MyApp --output ./MyApp \
  --use-config-source false
cd MyApp
dotnet dev-certs https --trust
az login
aspire run
```

Use the **full repository URL**: the current CLI passes this value to Git, so the bare
`aspire-entra-template` shorthand is not supported. `--use-config-source false` selects
the template without an organization-provided configuration overlay. The daily CLI
automatically writes the generated app's NuGet configuration; do not add `--channel daily`
to this repository-template command (the CLI does not allow that combination).

Choose a test framework when prompted (xUnit by default), or `None`. The template also
supports `.dev.localhost` URLs. HTTPS is mandatory; HTTP-only and Redis/output-cache
options are intentionally omitted. Names, namespaces, ports and the AppHost user-secrets
ID are transformed for each generated app.

At first run, select your Azure subscription, location and a **dedicated development
resource group**. Aspire provisions the app registration and service principal in the
subscription's tenant, even for a local run. It injects the registration's tenant and
client IDs into the frontend and registers its HTTPS sign-in and sign-out callbacks.
Open the **webfrontend HTTPS URL** in the dashboard; you will be redirected to Entra.
After signing in, try Counter and Weather, then use the POST-based **Sign out** button.

### Keep registrations isolated

In the Azure context dialog, set a unique **Tenant-scoped resource suffix**, such as
`myapp-alex-dev`, **before provisioning**. It can also be initialized with the AppHost
configuration key `Azure:TenantResourceSuffix`. Use a different suffix for each developer,
app and deployed environment; the logical registration resource is named `entra-web`.

Registration identity is tenant-scoped, **not resource-group-scoped**. Reusing a suffix
targets the same registration and can overwrite another environment's callback settings.
Changing the suffix creates another registration; it does not delete the previous one.

## Publish and deploy

From the generated app directory:

```bash
# Generate Bicep and deployment artifacts only; does not provision Azure resources.
aspire publish

# Provision Azure resources, build/push images and deploy (incurs Azure charges).
aspire deploy
```

Choose a dedicated deployment resource group and a **different** tenant resource suffix
from local development. Interactive deployment prompts for the optional suffix; published
Bicep exposes `tenantResourceSuffix` (default empty), not your local saved suffix.
For unattended deployment, explicitly configure the suffix rather than sharing the empty
default. The integration derives deployed callbacks from the frontend's Container Apps
hostname, not the local development port.

Only `webfrontend` has external ingress. `apiservice` accepts calls inside the Container
Apps environment and is called server-side using service discovery. It does **not** validate
user access tokens: network isolation is the boundary, not delegated API authorization.
Do not expose the API publicly without adding an appropriate authentication/authorization
design. Local development endpoints are accessible on the developer machine.

## Authentication and hosting choices

- Microsoft.Identity.Web validates Entra ID tokens (issuer, audience, lifetime and nonce)
  and issues a secure application cookie. This deliberately uses the playground's
  **ID-token-only OIDC flow** (`response_type=id_token`, `response_mode=form_post`).
  It is sign-in, not authorization-code redemption or an API access-token flow.
  Tenants that prohibit implicit ID-token issuance need a different flow with a properly
  configured client credential; no secret or authentication bypass is generated here.
- Blazor routes and interactive endpoints require authentication. `AuthorizeRouteView`
  also enforces authorization during circuit navigation. Sign-out requires a valid
  antiforgery token. Health checks and static assets remain public.
- Authenticated pages are **not output-cached**. There is no shared Redis/output cache.
- Container Apps terminates TLS. Forwarded headers retain ASP.NET Core's loopback-only
  trust defaults; arbitrary forwarded hosts/proxies are not trusted. In ACA, the
  platform-injected `CONTAINER_APP_HOSTNAME` pins the request's public host and HTTPS
  scheme so OIDC callbacks match the registration without trusting a changing proxy IP.
  The starter uses the default ACA hostname; custom-domain callbacks require deliberate
  registration and canonical-origin changes.
- The web app is limited to one replica for Blazor circuit routing. The generated ACA
  resource enables the platform's .NET data-protection auto-configuration; its persistence
  has not been exercised by the artifact-only checks. Local keys use ASP.NET Core defaults.
  Before scaling or using this beyond a starter, verify durable/shared data-protection
  keys, configure session affinity and review authorization/tenant policy. If keys change,
  cookies and antiforgery tokens become invalid; sign in again.

## Tests

For the default xUnit v2 project, NUnit, or xUnit v3 with VSTest, run:

```bash
dotnet test
```

MSTest and xUnit v3 with Microsoft Testing Platform use their native executable runner
instead (no `global.json` runner override is needed):

```bash
dotnet run --project Aspire-StarterApplication.1.Tests
```

The generated tests host only the frontend with test-only authentication, static OIDC
discovery and a fake internal weather response. They check anonymous challenges,
authenticated pages, POST/antiforgery logout and ACA HTTPS callback construction.
They do not create registrations, contact Azure, or prove a real browser login.

## Cleanup

Stop the local AppHost with `aspire stop`. Review and delete the dedicated Azure resource
groups when you no longer need them. `aspire destroy` deletes the **entire deployment
resource group**, including resources that Aspire did not create.

**Resource group deletion does not delete tenant-scoped Entra objects.** In the Entra
admin center, identify the exact application registration and its enterprise application
(service principal) by the client/object IDs shown in the Aspire dashboard, verify they
are not shared, and delete those objects explicitly. Repeat for obsolete suffixes.
Do not bulk-delete registrations by display-name prefix.

## Template provenance

Adapted from [mitchdenny/aspire-template-test-aspire-starter](https://github.com/mitchdenny/aspire-template-test-aspire-starter)
and the [Aspire Entra playground](https://github.com/microsoft/aspire/tree/main/playground/EntraId).
The starter's MIT license is preserved in [LICENSE.TXT](LICENSE.TXT); bundled Bootstrap
assets retain their copyright/license notices.
