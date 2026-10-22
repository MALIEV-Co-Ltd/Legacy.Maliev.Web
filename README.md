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

Web adopts Legacy.Maliev.ServiceDefaults at `4ffba0639620e7e923d331317f3f28501526b573` in the normal build, Dockerfile and joined Web graph. CompatibilityContracts remains `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; dedicated historical producer proof graphs retain their own pins.

Actual Program registers `AddPrivateRequestObservation("web")`. GET `/web/liveness` and `/web/readiness` identify the serving host with an uncached process marker. The private `/internal/diagnostics/observability` probe requires the original connection peer to be loopback and exactly one valid nonce; forwarded headers cannot grant access. This source draft requires hosted consumer and joined validation before acceptance.


### Private error-observation source disposition

Source [8175e8f3](https://github.com/MALIEV-Co-Ltd/maliev-web/commit/8175e8f3383d31030de2321c480bf6f5bc0fbb2b)
and the later privacy change [f14d3ef9](https://github.com/MALIEV-Co-Ltd/maliev-web/commit/f14d3ef9c294783a34d1196050f51f682aab8316)
are mapped to the current Program, private Web console formatter, incident boundary
and pinned request-observation producer. The source operational document is
[docs/production-observability.md at 8175e8f3](https://github.com/MALIEV-Co-Ltd/maliev-web/blob/8175e8f3383d31030de2321c480bf6f5bc0fbb2b/docs/production-observability.md).
The mapping below preserves its future operational requirements without running
the diagnostic against production or enabling deployment or Cloud collection.

| Source document lines | Current contract and disposition |
| --- | --- |
| 1–8 | Native console JSON maps warning/error/critical to WARNING/ERROR/CRITICAL. The private formatter retains bounded code/type/location, trace and assembly-version metadata, omits rendered messages/customer values, and keeps successful activity quiet. |
| 10–19 | The diagnostic uses the original socket peer before forwarding, GET and exactly one canonicalizable 32-hex nonce. Denial is quiet 404; repeats within one minute are quiet 429. Admission emits Warning, the real SDK Error and Critical with validated synthetic correlation, bypassing business work. |
| 21–26 | The source per-pod loopback forwarding/stdout/Cloud readback procedure remains an inactive operational requirement. Any future authorized proof must own and release each forward on success/failure, inspect every ready serving pod, and independently join the nonce and revision across stdout and Cloud records within the source 180-second window. No production ingestion is established here. |
| 28–33 | A future operational visibility check must fail closed on unverified pods, mixed revisions, public admission, missing correlation or missing/misclassified records. Quiet natural logs alone cannot establish ingestion. Historical LogLevel overlays remain a readback concern. |
| 35–42 | Only the exact controlled diagnostic exception or warning event plus validated synthetic flag/nonce qualifies as synthetic. Real failures remain separate; any future 24-hour/seven-day comparison must deduplicate trace/incident identities and reject truncation. Diagnostic ingestion does not establish customer-workflow success. |

The shared producer deliberately returns an empty 500 with
application/problem+json, canonical X-Maliev-Diagnostic-Id and a stable
X-Maliev-Health-Instance for the admitted synthetic branch, without inventing an
X-Incident-Id. Ordinary Web failures retain the separate opaque incident owner.
This preserves the accepted producer/consumer response contract rather than
copying the source application's synthetic incident body. Completed health
failures use the fixed HealthProbeFailure/Readiness-or-Liveness grammar, retain
five-minute same-status suppression, log a status change immediately, and reset
after any response below 500. Other completed 5xx use
HandledOperationFailure/HttpResponse; thrown failures retain one incident owner.

The source deployment-script revision change is covered by the existing, gated
publisher: [.github/workflows/publish-image.yml](.github/workflows/publish-image.yml)
pins the shared publisher at d583f55473f47f72d33b51060fa5d14e0974daf5. Its source
validates a full 40-hex commit, uses that commit as the image tag and OCI revision
label, verifies the packaged label, and records the immutable image digest and
source revision. The original short revision tag is superseded by the full SHA.
These are inactive source/build contracts; no image publication, deployment or
serving-pod revision is claimed.

The existing console-exclusive regression collection adds one real Kestrel
socket case alongside the retained Program/privacy cases. It uses an OS-assigned
loopback port, no injected connection peer, actual native console JSON, the
registered health marker, method/nonce denial, canonical admission and quiet
throttling. The new case is authored, NOT RUN at this documentation revision.
Existing TestServer controls remain separate; exact-head hosted build/tests,
unchanged full/format/security/coverage gates and protected-main acceptance are
still required before source closure.
