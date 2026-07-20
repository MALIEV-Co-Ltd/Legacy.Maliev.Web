# Instant Quotation Production-Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore the production upload-first Instant 3D Quotation journey for issues #148-#152 without duplicating FileService #7 or #153/#154 work.

**Architecture:** Keep the route/document as static SSR and mount one Interactive Server workflow. Store authoritative quote state server-side, consume FileService through an explicit typed client, derive all prices/totals from stored authoritative geometry, and submit through existing quotation-service/auth/idempotency patterns.

**Tech Stack:** .NET 10, ASP.NET Core Razor Components Interactive Server, xUnit, typed `HttpClient`, Redis-backed protected state, JavaScript modules, Three.js loaders, OCCT assets, esbuild, localization resources.

## Global Constraints

- Work only in `B:\maliev\.worktrees\web-instant-quotation-impl` on `codex/instant-quotation-implementation`.
- Do not edit, stage, or claim #153 artifacts in `B:\maliev\.worktrees\web-instant-quotation`.
- Do not implement FileService #7, #153 validation, #154 Aspire startup, port 5088, deployment, infrastructure, or production writes.
- Do not add ML/Prediction, Barcode, PayPal, Omise, jQuery, jquery-validation, WOW.js, Animate.css, browser credentials, or PII-bearing analytics.
- Every production behavior starts with a failing focused test and ends with a coherent validated commit.

---

### Task 1: Typed workflow state and deterministic server pricing

**Files:**
- Create: `Legacy.Maliev.Web.Application/InstantQuotationContracts.cs`
- Create: `Legacy.Maliev.Web.Application/InstantQuotationPricingService.cs`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationWorkflowPricingTests.cs`
- Modify: `Legacy.Maliev.Web.Application/Pricing/PricingCatalog.cs` only if a captured material/color compatibility rule is absent

**Interfaces:**
- Produces `InstantQuotationPart`, `InstantQuotationGeometry`, `InstantQuotationPartConfiguration`, `InstantQuotationPartQuote`, `InstantQuotationOrderQuote`, and `IInstantQuotationPricingService.Quote(InstantQuotationOrderState)`.
- Geometry contains dimensions, volume, footprint, area/perimeter profiles, weight/bounding inputs, facet/body/manifold metadata, and FileService authority marker.

- [ ] Write failing tests proving material keys, material/color compatibility, quantities `1/9/10/49/50/99/100/1000`, multi-part FDM/resin totals, shipping/VAT/five-baht rounding, and `1-(n+2)` lead-time range.
- [ ] Add a tampering test where browser-provided subtotal/weight/time values differ from stored geometry and assert the service ignores them.
- [ ] Run `dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev --filter FullyQualifiedName~InstantQuotationWorkflowPricingTests` and confirm the expected missing-type failures.
- [ ] Implement the records and pricing service as a thin orchestrator over `PricingEngine`, `PricingCatalog`, and `ShippingCalculator`.
- [ ] Rerun the focused tests and existing `InstantQuotationPricingTests`; require zero failures.
- [ ] Commit `feat(web): add authoritative instant quote pricing state`.

### Task 2: Protected quotation session and exact FileService client boundary

**Files:**
- Create: `Legacy.Maliev.Web.Application/InstantQuotationUploadContracts.cs`
- Create: `Legacy.Maliev.Web.Infrastructure/InstantQuotationSessionStore.cs`
- Create: `Legacy.Maliev.Web.Infrastructure/InstantQuotationUploadClient.cs`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationSessionStoreTests.cs`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationUploadClientTests.cs`
- Modify: `Legacy.Maliev.Web.Infrastructure/ServiceCollectionExtensions.cs`

**Interfaces:**
- Produces `IInstantQuotationSessionStore` with `CreateAsync`, `GetAsync`, `PutAsync`, and `RemoveAsync` over protected Redis state.
- Produces `IInstantQuotationUploadClient.UploadAsync`, `RemoveAsync`, and `FinalizeAsync` matching FileService #7 OpenAPI examples exactly.
- Until #7 publishes exact examples, tests define the Web-owned abstraction and the HTTP adapter remains fail-closed rather than guessing endpoint paths or response fields.

- [ ] Write failing session tests for cryptographically random identity, expiry, cross-session denial, state preservation, and no access/refresh token serialization.
- [ ] Write failing HTTP contract tests from FileService #7 published examples for multipart name/casing, idempotency, problem details, opaque references, cancellation, 401/403, 409, 413, 415, 422, 503, and finalization replay.
- [ ] Run both focused classes and confirm expected failures.
- [ ] Implement protected distributed state following `AccountSessionStore`; implement the client only against published #7 wire examples.
- [ ] Rerun focused tests and `QuotationClientTests`; require zero failures.
- [ ] Commit `feat(web): add isolated instant quote upload session boundary`.

### Task 3: Static SSR shell and scoped Interactive Server workflow

**Files:**
- Create: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor`
- Create: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor.cs`
- Create: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflowState.cs`
- Modify: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationPage.razor`
- Modify: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/ThreeDimensionalPrintingEstimateContent.razor`
- Modify: `Legacy.Maliev.Web/Program.cs`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationInteractiveRouteTests.cs`

**Interfaces:**
- Static page owns metadata/document shell; `InstantQuotationWorkflow` is the sole `InteractiveServer` island.
- Workflow consumes session, upload, pricing, country, quotation, antiforgery, and analytics services through dependency injection.

- [ ] Write failing route/source tests proving the shell remains statically renderable, only the workflow is interactive, no manual geometry fields remain in the primary journey, and no other route gains an interactive render mode.
- [ ] Write failing component-state tests for empty, uploading, uploaded, error, multi-part, configured, review, and submitted states.
- [ ] Run focused tests and verify expected failures.
- [ ] Register Interactive Server services/render mode without changing other route ownership; implement empty/upload/config/review/customer/submitted state composition.
- [ ] Rerun `InstantQuotationInteractiveRouteTests`, `InstantQuotationStaticSsrRouteTests`, `BlazorHostFoundationTests`, and `ArchitectureTests`.
- [ ] Commit `feat(web): restore scoped instant quote workflow`.

### Task 4: Upload transport, viewer, multi-part state, and advisory DFM

**Files:**
- Create: `Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/upload-controller.mjs`
- Create: `Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/model-viewer.mjs`
- Create: `Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/geometry-analysis.mjs`
- Create: `Legacy.Maliev.Web/tests/instant-quotation-upload-controller.test.mjs`
- Create: `Legacy.Maliev.Web/tests/instant-quotation-viewer.test.mjs`
- Modify: `Legacy.Maliev.Web/package.json`
- Modify: `Legacy.Maliev.Web/assets/app-entry.js`

**Interfaces:**
- JS returns advisory preview metadata only; server state remains authoritative.
- Upload controller reports progress/cancel/retry and uses antiforgery; viewer supports reset, fit, fullscreen, orbit, keyboard alternative, selection/removal, and cleanup.

- [ ] Write failing Node tests for all supported extensions, unsupported/malformed files, cancellation/retry, multiple independent parts, stale callback suppression, camera-state retention, multi-body color lock, DFM messages, and disposal.
- [ ] Run `npm run test:browser-module` and verify the new tests fail for missing modules.
- [ ] Implement dynamically imported loaders and reuse bundled OCCT assets; pin any added Three.js package in `package-lock.json`.
- [ ] Keep 64/24 profile sampling, 200k topology cap, 250k profile cap, `<3 mm`/`>350 mm` DFM thresholds, and non-watertight fallback only as advisory compatibility behavior.
- [ ] Run `npm run ci`, `npm audit --omit=dev`, and `npm audit`; require zero known vulnerabilities.
- [ ] Commit `feat(web): restore instant quote viewer and multi-part upload state`.

### Task 5: Review, customer details, CSRF, idempotent submission, and finalization

**Files:**
- Create: `Legacy.Maliev.Web.Application/InstantQuotationSubmissionContracts.cs`
- Create: `Legacy.Maliev.Web.Application/InstantQuotationSubmissionService.cs`
- Create: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationReview.razor`
- Create: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationCustomerForm.razor`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationSubmissionTests.cs`
- Modify: `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor.cs`

**Interfaces:**
- Submission consumes only server session state plus validated customer fields and stable `SubmissionId`.
- It produces `{ requestId, submissionStatus }`, calls existing `IQuotationClient` with `legacy-web-instant-quotation-{SubmissionId.ToLowerInvariant()}` for the protected 64-hex submission identity, then calls FileService finalization.

- [ ] Write failing tests for required customer fields, anonymous/member prefill, invalid/missing antiforgery, session mismatch, tampered totals, duplicate submit, quotation timeout, persisted-request partial finalization, and stable reference messaging.
- [ ] Assert exact quotation-service camelCase JSON and that browser tokens/storage secrets never appear in rendered markup or JSON.
- [ ] Run the focused tests and verify expected failures.
- [ ] Implement server recomputation, request creation, FileService finalization, PRG-equivalent persisted state, and fail-closed retry behavior.
- [ ] Rerun focused tests plus `QuotationPageTests` and `QuotationClientTests`.
- [ ] Commit `feat(web): restore instant quotation review and submission`.

### Task 6: Thai/English, accessibility, responsive presentation, and consent analytics

**Files:**
- Modify: `Legacy.Maliev.Web/Resources/Components/Pages/InstantQuotation/ThreeDimensionalPrintingEstimateContent.th.resx`
- Create: `Legacy.Maliev.Web/Resources/Components/Pages/InstantQuotation/InstantQuotationWorkflow.th.resx`
- Modify: `Legacy.Maliev.Web/wwwroot/src/app/css/instant-quotation.css`
- Create: `Legacy.Maliev.Web/wwwroot/src/app/js/instant-quotation/analytics.mjs`
- Create: `Legacy.Maliev.Web/tests/instant-quotation-analytics.test.mjs`
- Create: `Legacy.Maliev.Web.Tests/InstantQuotationAccessibilityAnalyticsTests.cs`

**Interfaces:**
- Keeps the frozen `file_upload_start`, `file_upload_complete`, and persisted `request_quote` payloads exact. The pending `upload_failure`, `estimate_shown`, and `review_reached` emitters remain inactive until their exact tests and independent review pass; `file_upload_failure` is forbidden.
- `request_quote.transaction_id` is the opaque persisted request identifier; payloads exclude filename, session ID, customer fields, and description.

- [ ] Write failing tests for Thai/English metadata/copy/validation, localized THB formatting, native control labels/focus order/live regions, reduced motion, 760px mobile layout, consent queueing, event deduplication, and no PII.
- [ ] Run focused xUnit and Node tests and verify expected failures.
- [ ] Implement production hierarchy, corrected accessible semantics, mobile/sticky layout, localization, and consent-gated analytics.
- [ ] Rerun focused tests, `PublicGoogleTagManagerMigrationTests`, `PublicCookieConsentMigrationTests`, and `AnalyticsSurfaceContractTests`.
- [ ] Commit `feat(web): complete instant quote localization accessibility and analytics`.

### Task 7: Integration verification, PR, CI, and coordination

**Files:**
- Modify only implementation-owned documentation if verification commands or contract links need recording.
- Do not modify #153 fixtures/release-gate files.

- [ ] Run focused InstantQuotation xUnit and Node suites from a clean tree.
- [ ] Run `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore -p:MalievWorkspaceRoot=B:\maliev`.
- [ ] Run `dotnet test Legacy.Maliev.Web.slnx -c Release --no-restore -p:MalievWorkspaceRoot=B:\maliev`; if the pre-existing Redis/container hang recurs, identify the exact test and report it separately.
- [ ] Run `npm run ci`, production dependency audit, NuGet vulnerability audit, `gitleaks detect --no-banner --redact`, and repository secret scan.
- [ ] Run browser QA on an isolated non-5088 local port for English/Thai, 1440x900 and 390x844, keyboard-only, reduced-motion, multi-part/retry/review/submitted states, console, network, and accessibility.
- [ ] Rebase on current `origin/main`, rerun required verification, push `codex/instant-quotation-implementation`, and open a PR linking #148-#152 and FileService #7.
- [ ] Update issues #148-#152 and Project #2 with commit/PR/test evidence; leave deployment blocked pending #153/#154 and owner Aspire review.
- [ ] Own the exact pushed commit through GitHub Actions; use the CI-fix workflow for any failures until green or a concrete external blocker is documented.
