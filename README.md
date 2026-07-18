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

Customer account routes use the modern AuthService login, rotating refresh,
revocation, registration, email-confirmation, and password-recovery JSON APIs.
The browser receives only a secure, HTTP-only, host-only opaque cookie. Access and
refresh tokens are data-protected in the existing Redis deployment; Redis also
coordinates refreshes across replicas so a single-use refresh token is never
replayed concurrently. Data-protection keys use the same Redis deployment and no
new database, node pool, or managed cloud service.

Signup is a compensating BFF workflow: it creates the CustomerService profile,
creates the AuthService identity, deletes the profile if identity creation fails,
then sends a one-time confirmation link through NotificationService. Password
recovery never reveals whether an account exists. The `legacy-web` service identity
therefore requires only `legacy-auth.customer-self-service`,
`legacy-customer.customers.create`, `legacy-customer.customers.delete`, and the
previously documented contact, quotation, file, and notification permissions.
Authenticated Member address management additionally uses the server-held
`legacy_database_id` from the Auth-issued access token and requires only
`legacy-customer.customers.read`, `legacy-customer.customers.update`,
`legacy-customer.addresses.create`, and `legacy-customer.addresses.update`.
The BFF reloads the customer and address IDs
from CustomerService before every write; browser form data cannot select another
customer or address record.
Member profile management follows the same ownership rule and reloads customer,
company, address, and relationship IDs before every write. Company operations
use only the corresponding least-privilege CustomerService permissions. Email
and password changes use the customer's short-lived access token, verify the
current password in AuthService, revoke all refresh sessions, and return the
browser to login; raw credentials never appear in logs or URLs. The legacy
passwordless `CreatePassword` route redirects to the supported password-change
flow because the migrated identity boundary has no external-login-only account.
Runtime Redis and service credentials are projected from the single
`maliev-legacy-secrets` secret; source configuration contains no credential. The
same projection supplies `DataProtection__CertificatePfxBase64` and
`DataProtection__CertificatePassword`, which encrypt the shared Redis key ring at
rest. Production fails closed if Redis or this certificate material is missing.

## Local validation

The repository is designed to run under `Legacy.Maliev.AppHost`, which reflects the existing GKE service topology without provisioning cloud resources.

```powershell
dotnet restore .\Legacy.Maliev.Web.slnx -p:MalievWorkspaceRoot=B:\maliev
dotnet build .\Legacy.Maliev.Web.slnx -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev
dotnet test .\Legacy.Maliev.Web.slnx -c Release --no-build -p:MalievWorkspaceRoot=B:\maliev
```

## Migration status

The standalone .NET 10 BFF foundation, Scalar/OpenAPI endpoint, health endpoints, resilient service-client boundary, and security architecture gates are active. All twenty-two indexed legacy routes, the public Account surface, login, signup, email confirmation, password recovery, and Member profile/address/credential routes are migrated, together with the localized XML sitemap. Runtime integration and deployment readiness are tracked in [MALIEV Legacy Migration Project #2](https://github.com/orgs/MALIEV-Co-Ltd/projects/2).

Deployment is intentionally deferred until route compatibility, external credential rotation, live GTM malware review, and existing-cluster capacity gates are complete.
