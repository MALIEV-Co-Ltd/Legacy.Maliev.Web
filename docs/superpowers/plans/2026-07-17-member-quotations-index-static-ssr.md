# Member Quotations Index Static SSR Migration Plan

## Goal

Move the authenticated customer quotation listing to localized, display-only
.NET 10 Blazor static SSR while preserving ownership, query, and paging behavior.

## Tasks

- [x] Add failing source, authorization, localization, query, paging, and secret-leak tests.
- [x] Project service results into a narrow server-built display model.
- [x] Extract listing, search, errors, and pagination into a non-interactive component.
- [x] Move localization and add exact Thai coverage for all current strings.
- [ ] Run full validation, independent review, PR checks, exact-SHA main CI and
  image verification, then update Project #2 and issue #29.
