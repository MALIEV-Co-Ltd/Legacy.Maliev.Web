# Legacy.Maliev.Web

Public clean-history migration of the MALIEV website from the private `maliev-web` monorepo.

## Architecture

- `Legacy.Maliev.Web` — .NET 10 Blazor Web App and same-origin backend-for-frontend. Public and read-only account routes use static SSR; narrowly scoped Razor POST handlers retain authoritative server-side writes, antiforgery, and rollback compatibility.
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

The reproducible local resource and latency measurement command and the Razor Pages/jQuery-to-Blazor comparison are recorded in [the migration performance evidence](docs/blazor-migration-performance.md). These checks use loopback/Aspire only and provision no cloud resources.

## Migration status

The standalone .NET 10 Blazor Web App/BFF foundation, Scalar/OpenAPI endpoint, health endpoints, resilient service-client boundary, and security architecture gates are active. All indexed public routes and authenticated Member GET routes are Blazor static SSR route owners; server-side write handlers remain isolated behind POST, authorization, antiforgery, and downstream ownership checks. The localized XML sitemap, consent-gated analytics bridge, and legacy URL aliases are covered by regression tests. Runtime integration and deployment readiness are tracked in [MALIEV Legacy Migration Project #2](https://github.com/orgs/MALIEV-Co-Ltd/projects/2).

Production deployment is intentionally deferred for owner review in Aspire and explicit cutover approval. Live GTM/GA4/Ads/Search Console verification remains an external-console review item and is not inferred from local tests.

## Current shared dependency and private observation

Web adopts Legacy.Maliev.ServiceDefaults at `c9f21151639f1911c780f2084dd2d757b4fb3e73` in the normal build, Dockerfile, current Web qualification consumers and joined Web graph. CompatibilityContracts remains `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; dedicated historical producer proof graphs retain their own pins.

Actual Program registers `AddPrivateRequestObservation("web")`. GET `/web/liveness` and `/web/readiness` identify the serving host with an uncached process marker. The private `/internal/diagnostics/observability` probe requires the original connection peer to be loopback and exactly one valid nonce; forwarded headers cannot grant access. This source draft requires hosted consumer and joined validation before acceptance.

## Private Web console boundary

Web selects the existing `PrivateFailureConsoleFormatter` from the selected
shared producer `c9f21151639f1911c780f2084dd2d757b4fb3e73` after `AddServiceDefaults`. Only the built-in
console logging provider is retained; raw OpenTelemetry log export is excluded.
The shared metrics and tracing registrations and configured exporters remain
unchanged. No other service's default logging provider is changed.

This is the native-console adaptation of original
`f14d3ef9c294783a34d1196050f51f682aab8316`, not a claim that every logging
provider filters customer data. The formatter suppresses Information, Debug and
Trace records even when console category configuration enables them. Failure
records use a fixed message, runtime UTC/assembly/exception provenance, actual W3C
trace/span correlation and bounded scalar fields. Messages, request values,
objects and raw scopes are not serialized.

The five source business codes remain exact. Modern response incidents retain
their nonempty GUID-N identifier and exact round-trip incident time normalized to
UTC; historical arbitrary request trace identifiers are not copied. The
formatter's lowercase timestamp and runtime provenance remain authoritative.
The Web incident logger passes the original exception only to this selected
private sink so its runtime type can be preserved without its message or stack.

Ordinary non-Testing entry-point failures use `PrivateStartupBoundary` and set
exit code 1. Testing startup validation and cancellation failures are rethrown
unchanged without changing the process exit code, so WebApplicationFactory
observes the original failed startup rather than successful completion.
`HostAbortedException` is rethrown unchanged for host discovery/tooling. Tests use
an isolated entry writer and the actual DI-selected console formatter without
replacing process-wide console streams. Existing request observation continues
to own handled-failure and bounded/resettable health records.

The selected shared producer comes from PR #93. Its exact protected-main checks
must be green before this Web follower is published. The Web consumer and
dependency-pin proposal still requires its own reviewed exact-head
Release, focused/full, formatting, security and coverage checks, followed by
protected-main verification. Neither source #469 nor the
separate diagnostic issue #466 is closed by source inspection or by this README.
