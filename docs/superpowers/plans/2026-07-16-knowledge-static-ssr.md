# Knowledge Area Static SSR Migration Plan

## Goal

Move all seven public Knowledge route bodies from Razor markup to localized,
non-interactive .NET 10 Blazor static SSR without changing their public URL,
SEO metadata, navigation, analytics shell, or responsive behavior.

## Tasks

- [x] Add failing route, renderer, localization, and no-interactivity contracts.
- [x] Extract the Knowledge index, guidelines, workflow, and four specification
  bodies into focused Razor components.
- [x] Move Thai component resources and use the same typed localizers for route
  metadata so titles and descriptions remain localized.
- [x] Preserve canonical lower-case links to Knowledge, legal, and service routes.
- [x] Fix the Knowledge action-link contrast regression found during visual QA.
- [x] Run focused and full tests, Release build, formatting, deterministic asset
  build, dependency audits, and English/Thai desktop/mobile browser checks.
- [ ] Complete independent review, PR checks, merge, exact-SHA main CI and image
  verification, then record evidence in Project #2 and issue #29.
