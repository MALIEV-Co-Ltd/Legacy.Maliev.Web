# Member Landing Static SSR Migration Plan

## Goal

Move the authenticated member landing body to a localized, display-only .NET 10
Blazor static SSR component while keeping data loading, authorization, and service
boundaries in the server PageModel.

## Tasks

- [x] Add failing source, authorization, localization, route-link, and secret-leak tests.
- [x] Introduce a narrow display model and map existing profile/order/quotation results server-side.
- [x] Extract the member overview body into a non-interactive Razor component with canonical links.
- [x] Move component localization and repair the missing current Thai translations.
- [ ] Run full validation, authenticated browser QA, independent review, PR checks,
  exact-SHA main CI and image verification, then update Project #2 and issue #29.
