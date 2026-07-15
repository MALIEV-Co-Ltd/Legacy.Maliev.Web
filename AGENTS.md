# Legacy.Maliev.Web agent guidance

- Preserve the public and member URL contracts, SEO metadata, canonical and alternate-language links, and Thai/English behavior captured from `maliev-web`.
- Keep this repository a server-rendered Razor Pages frontend and backend-for-frontend. It must not reference a legacy `DbContext`, data project, EF Core provider, PayPal, PDF renderer, or database logger.
- Cross-domain reads and writes use typed HTTP clients and explicit DTO contracts. Authentication tokens and refresh tokens stay server-side; never expose service tokens to browser JavaScript.
- Preserve consent-safe GTM, GA4, Google Ads, and Search Console instrumentation. No Universal Analytics, PII-bearing analytics events, or tags loaded before consent.
- Google APIs use ADC or Workload Identity for server credentials. Browser identifiers must be configuration-driven and appropriately restricted. Never copy credentials or private source history.
- Barcode rendering, if retained, is a browser concern. Payment UI must not integrate Omise until the dedicated future workflow is approved, and all PayPal behavior is removed.
- Use .NET 10, Scalar/OpenAPI, standard MALIEV service defaults, built-in structured logging, output caching, response compression, and resilient typed clients.
- Existing GKE and namespace `maliev-legacy` only. Do not add node pools, Cloud SQL, or other paid infrastructure.
- Validate release build, route/SEO/analytics/auth contract tests, browser accessibility, dependency vulnerabilities, container health, and gitleaks before coherent commits.
