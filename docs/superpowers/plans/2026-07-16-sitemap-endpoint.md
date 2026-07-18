# Sitemap Endpoint Migration Plan

**Goal:** Remove the sitemap Razor Page shim while preserving the machine-readable SEO XML contract.

**Architecture:** Map `/Sitemap` directly through an endpoint extension that renders `PublicSearchRouteCatalog.Routes`. This route intentionally does not use Blazor because XML sitemap consumers require a raw `application/xml` response, not HTML.

## Constraints

- Preserve case-insensitive `/sitemap` compatibility.
- Preserve 22 indexed routes, canonical locations, and three localized alternates per route.
- Preserve `application/xml; charset=utf-8` and exclude account routes.
- Exclude the machine endpoint from OpenAPI.
- Remove `Pages/Sitemap.cshtml` and its PageModel.
- No infrastructure, database, or paid-service changes.

## Tasks

- [x] Add failing endpoint architecture and XML response tests.
- [x] Implement and register the dedicated endpoint; remove the Razor shim.
- [x] Run focused/full release gates, live XML validation, and independent review.
- [ ] Commit, publish PR, update Project #2, merge after checks, and verify exact-SHA main CI/image.
