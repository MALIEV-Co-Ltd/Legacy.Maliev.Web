# Legacy.Maliev.Web

Public clean-history migration of the MALIEV website from the private `maliev-web` monorepo.

## Architecture

- `Legacy.Maliev.Web` — .NET 10 Razor Pages frontend and backend-for-frontend.
- `Legacy.Maliev.Web.Application` — browser-facing contracts and service endpoint options.
- `Legacy.Maliev.Web.Infrastructure` — resilient typed HTTP clients for independently deployed legacy services.
- `Legacy.Maliev.Web.Tests` — route, security, SEO, analytics, auth, and architecture compatibility gates.

The Web process owns no domain database and contains no PayPal, PDF generation, barcode service, or database logging implementation. Service credentials are supplied by Workload Identity/ADC and the consolidated `maliev-legacy-secrets` deployment contract; no credentials belong in this repository.

The public Contact form uses reCAPTCHA Enterprise through Application Default Credentials, reads countries anonymously from `Legacy.Maliev.CountryService`, and writes messages with a short-lived server-only service JWT. The deployment maps the Web client secret from `maliev-legacy-secrets` to `ServiceAuthentication__ClientSecret` and supplies the public reCAPTCHA site key as `Recaptcha__SiteKey`; neither value is stored in source. Unsafe HTTP methods are not retried automatically, preventing duplicate contact records.

The manual Quotation form uses a stable per-form idempotency key, streams optional files through `Legacy.Maliev.FileService` quarantine and malware scanning, and stores only the resulting bucket/object metadata in `Legacy.Maliev.QuotationService`. Browser uploads are limited to 10 files and 100 MB combined. If metadata linking fails, the Web BFF attempts to remove objects that were not linked; an already persisted quotation reference is always shown as received so the customer does not submit it twice.

Persisted Contact and Quotation requests trigger separate customer and internal messages through the authenticated NotificationService JSON API. Recipient data and bodies stay in JSON rather than URL query strings, user-provided values are HTML-encoded for internal email, and notification failure never hides an already persisted reference or tells the customer to resubmit it.

## Local validation

The repository is designed to run under `Legacy.Maliev.AppHost`, which reflects the existing GKE service topology without provisioning cloud resources.

```powershell
dotnet restore .\Legacy.Maliev.Web.slnx -p:MalievWorkspaceRoot=B:\maliev
dotnet build .\Legacy.Maliev.Web.slnx -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev
dotnet test .\Legacy.Maliev.Web.slnx -c Release --no-build -p:MalievWorkspaceRoot=B:\maliev
```

## Migration status

The standalone .NET 10 BFF foundation, Scalar/OpenAPI endpoint, health endpoints, resilient service-client boundary, and security architecture gates are active. All twenty-two indexed legacy routes and the localized XML sitemap are migrated. Runtime integration and deployment readiness are tracked in [MALIEV Legacy Migration Project #2](https://github.com/orgs/MALIEV-Co-Ltd/projects/2).

Deployment is intentionally deferred until route compatibility, external credential rotation, live GTM malware review, and existing-cluster capacity gates are complete.
