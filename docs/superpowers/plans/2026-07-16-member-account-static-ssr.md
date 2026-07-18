# Member Account Static SSR Migration Plan

## Goal

Move the authenticated member account landing body to localized, display-only
.NET 10 Blazor static SSR while preserving the server authorization boundary.

## Tasks

- [x] Add failing source, authorization, localization, route-link, and secret-leak tests.
- [x] Extract the member account body into a non-interactive localized Razor component.
- [x] Move component localization and repair missing current Thai translations.
- [ ] Run full validation, independent review, PR checks, exact-SHA main CI and
  image verification, then update Project #2 and issue #29.
