# Legacy.Maliev.Web

Public clean-history migration of the MALIEV website from the private `maliev-web` monorepo.

## Architecture

- `Legacy.Maliev.Web` — .NET 10 Razor Pages frontend and backend-for-frontend.
- `Legacy.Maliev.Web.Application` — browser-facing contracts and service endpoint options.
- `Legacy.Maliev.Web.Infrastructure` — resilient typed HTTP clients for independently deployed legacy services.
- `Legacy.Maliev.Web.Tests` — route, security, SEO, analytics, auth, and architecture compatibility gates.

The Web process owns no domain database and contains no PayPal, PDF generation, barcode service, or database logging implementation. Service credentials are supplied by Workload Identity/ADC and the consolidated `maliev-legacy-secrets` deployment contract; no credentials belong in this repository.

## Local validation

The repository is designed to run under `Legacy.Maliev.AppHost`, which reflects the existing GKE service topology without provisioning cloud resources.

```powershell
dotnet restore .\Legacy.Maliev.Web.slnx -p:MalievWorkspaceRoot=B:\maliev
dotnet build .\Legacy.Maliev.Web.slnx -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev
dotnet test .\Legacy.Maliev.Web.slnx -c Release --no-build -p:MalievWorkspaceRoot=B:\maliev
```

## Migration status

The standalone .NET 10 BFF foundation, Scalar/OpenAPI endpoint, health endpoints, resilient service-client boundary, and security architecture gates are active. Route-by-route legacy rendering and behavior migration is tracked in [MALIEV Legacy Migration Project #2](https://github.com/orgs/MALIEV-Co-Ltd/projects/2).

Deployment is intentionally deferred until route compatibility, external credential rotation, live GTM malware review, and existing-cluster capacity gates are complete.
