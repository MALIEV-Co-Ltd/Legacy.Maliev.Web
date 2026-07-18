# Logout Static SSR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate `/Account/Logout` to a localized Blazor static SSR confirmation surface while preserving the authorized, antiforgery-protected server sign-out boundary.

**Architecture:** A display-only `LogoutContent` component renders localized confirmation copy and a safe account link. The Razor page keeps the POST form so ASP.NET Core generates the antiforgery token, and its authorized PageModel remains solely responsible for refresh-token revocation, opaque-session removal, authentication-cookie clearing, and the home redirect.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages BFF, Blazor static SSR, `IStringLocalizer<T>`, xUnit, `WebApplicationFactory`.

## Global Constraints

- Preserve `/Account/Logout`, `[Authorize]`, `IAccountSessionManager.SignOutAsync`, and redirect to `/`.
- Keep access tokens, refresh tokens, session identifiers, service credentials, and customer identifiers out of the component and HTML.
- Keep the POST form in the server-rendered Razor page with antiforgery; reject tokenless POSTs.
- Add `no-store` and `no-referrer` to the authenticated confirmation response.
- Render the component with `render-mode="Static"`; do not load `blazor.web.js`.
- Localize all visible English and Thai confirmation text and page title.
- Use no new infrastructure, direct database access, or paid service.

## Tasks

- [x] Add failing architecture, localization, antiforgery, and sign-out parity tests.
- [x] Add the display-only static SSR component and component-owned Thai resource.
- [x] Keep the form/PageModel server boundary and add authenticated response headers.
- [x] Run focused tests to green, then the full release, browser, audit, and review gates.
- [ ] Commit, push a ready PR, add it to Project #2, merge after checks, and verify exact-SHA main CI plus immutable image publication.
