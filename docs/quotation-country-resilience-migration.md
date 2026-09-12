# Quotation country lookup resilience migration

Source commit `8f2b647421acae53a0a04f927747652a97bd8714` keeps the public quotation
page available when the country service cannot be reached. In the .NET 10
architecture, `CountryClient` owns the HTTP boundary and converts transient
HTTP and timeout failures into an unavailable `ServiceResponse`; both the
Blazor static-SSR route and retained multipart Razor fallback consume that
result without throwing and render the existing non-field validation message.

Issue #233 adds route-level regression coverage for both renderers. Each route
must return HTTP 200 and expose the country-retrieval error when the typed client
reports the service unavailable. This preserves the source behavior without
copying its page-owned HTTP call into the migrated boundary.

No source repository, deployment configuration, database, or production
environment is changed by this migration slice.
