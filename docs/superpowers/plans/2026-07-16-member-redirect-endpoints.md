# Member Compatibility Redirect Endpoint Plan

## Goal

Replace five authenticated redirect-only Razor Pages with minimal .NET 10
endpoints while preserving their challenge and redirect contracts and removing
all obsolete PageModel artifacts.

## Tasks

- [x] Add failing source-removal and real HTTP authorization/redirect tests.
- [x] Map authenticated GET-only endpoints for the three retired service-order
  entries, create-password compatibility route, and retired payment-success route.
- [x] Preserve temporary 302 destinations, discard untrusted incoming query
  values, return empty bodies, and exclude compatibility endpoints from OpenAPI.
- [x] Remove the eleven obsolete Razor, PageModel, and shared compatibility files.
- [ ] Run full validation, independent review, PR checks, exact-SHA main CI and
  immutable-image verification, then update Project #2 and issue #29.
