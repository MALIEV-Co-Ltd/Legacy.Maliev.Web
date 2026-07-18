# Error Static SSR Implementation Plan

**Goal:** Migrate `/Error` to localized Blazor static SSR without exposing exception, request, referrer, authentication, or session data.

**Architecture:** `ErrorModel` remains the diagnostic boundary and preserves re-executed status codes. It projects only an `IsNotFound` flag and optional request ID into a display model. `ErrorContent` owns visible localized markup and safe navigation links.

## Constraints

- Preserve direct and re-executed 404/500 status codes.
- Preserve the generic 404 and application-error messages and optional request ID.
- Never project exception details, referrer, user claims, headers, tokens, session IDs, or customer identifiers.
- Add `no-store`, `no-referrer`, and keep `noindex,nofollow`.
- Use static SSR without `blazor.web.js`.
- Localize every visible English/Thai string and title.
- No infrastructure, database, or paid-service change.

## Tasks

- [x] Add failing architecture, status, localization, header, and no-leak tests.
- [x] Add the safe display model, component, and component-owned Thai resource.
- [x] Preserve PageModel status/request-ID behavior and add secure response headers.
- [x] Run focused/full release gates, English/Thai 390px and desktop browser checks, and independent review.
- [ ] Commit, publish PR, update Project #2, merge after checks, and verify exact-SHA main CI/image.
