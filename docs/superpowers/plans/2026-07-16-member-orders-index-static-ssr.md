# Member Orders Index Static SSR Migration Plan

## Goal

Move the authenticated member order hub to localized, display-only .NET 10
Blazor static SSR while preserving authorization and canonical destinations.

## Tasks

- [x] Add failing source, authorization, localization, link, and secret-leak tests.
- [x] Extract the order hub into a non-interactive localized Razor component.
- [x] Move component localization with exact current Thai key parity.
- [ ] Run full validation, independent review, PR checks, exact-SHA main CI and
  image verification, then update Project #2 and issue #29.
