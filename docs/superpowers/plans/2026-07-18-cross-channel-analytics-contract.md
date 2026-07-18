# Cross-channel analytics contract implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `docs/analytics-validation.md` the authoritative, test-enforced contract for consent-safe GTM, GA4, Google Ads, Search Console, Maps, and social-channel measurement in Legacy.Maliev.Web.

**Architecture:** Keep measurement ownership split across the application data layer, GTM routing, and Google reporting surfaces. The application owns non-PII event names and parameters; GTM maps only consented events to GA4 and Ads; Search Console and Business Profile remain aggregate external signals joined in reporting, never browser data-layer events.

**Tech Stack:** Markdown runbook, .NET 10, xUnit contract tests, GitHub Actions.

## Global constraints

- Do not publish GTM, mutate Google consoles, deploy, or change GKE.
- Preserve `request_quote` as the only primary/biddable Ads conversion.
- All click, upload, Maps, review, and social diagnostics remain secondary/non-biddable.
- Do not collect PII, filenames, query strings, access tokens, refresh tokens, or session identifiers.
- Do not modify Instant Quotation implementation files or issue #153 validation harness files.
- Default Consent Mode v2 state is denied; the application must load GTM and flush queued events only after explicit grant.

---

### Task 1: Encode issue #23 acceptance criteria as a failing contract test

**Files:**
- Modify: `Legacy.Maliev.Web.Tests/AnalyticsSurfaceContractTests.cs`

**Interfaces:**
- Consumes: `docs/analytics-validation.md`.
- Produces: assertions for the dimension registry, channel taxonomy, weekly export schema, Search Console QA, consent semantics, and Ads primary/secondary policy.

- [x] **Step 1: Add one focused xUnit test that requires all issue #23 contract sections and controlled values.**
- [x] **Step 2: Run only `AnalyticsSurfaceContractTests` and verify the new test fails because the current runbook lacks those sections.**

### Task 2: Complete the authoritative analytics runbook

**Files:**
- Modify: `docs/analytics-validation.md`

**Interfaces:**
- Consumes: current application events (`request_quote`, upload events, contact/social click events, and `maliev_review_link_click`) plus the quotation-owner handoff.
- Produces: controlled custom-dimension values, channel classifications, consent behavior, UTM/referrer rules, weekly export columns, and cross-console QA evidence requirements.

- [x] **Step 1: Document the system boundary and exact default/update consent semantics.**
- [x] **Step 2: Add the GA4 event/parameter registry and explicitly classify every Ads outcome.**
- [x] **Step 3: Add custom-dimension definitions for `service`, `locale`, `contact_channel`, `lead_source`, `landing_page_type`, and `quote_flow_step`.**
- [x] **Step 4: Add Maps, social outbound, and referral attribution rules without inventing browser events for Search Console or Business Profile aggregate data.**
- [x] **Step 5: Add the weekly export schema and QA/evidence matrix for GTM Preview, GA4 DebugView, Ads diagnostics, Search Console URL Inspection, and Business Profile/Maps.**
- [x] **Step 6: Run the focused test and verify it passes.**

### Task 3: Validate and publish the coherent documentation slice

**Files:**
- Modify: `docs/analytics-validation.md`
- Modify: `Legacy.Maliev.Web.Tests/AnalyticsSurfaceContractTests.cs`
- Create: `docs/superpowers/plans/2026-07-18-cross-channel-analytics-contract.md`

**Interfaces:**
- Consumes: the completed runbook and tests.
- Produces: a reviewable issue #23 PR and Project #2 evidence without production changes.

- [x] **Step 1: Run the focused analytics tests, Release build, full test suite, format verification, dependency vulnerability audit, actionlint, and gitleaks.**
- [x] **Step 2: Review the diff for PII, secrets, live identifiers beyond already-public configuration, Instant Quotation implementation edits, and #153 harness overlap.**
- [ ] **Step 3: Commit one coherent validated slice, push it, open a PR closing #23, and record the PR/CI evidence in Project #2.**
- [ ] **Step 4: Merge only after required CI succeeds, then verify exact-main CI and mark the Project #2 item Done with deployment still deferred.**
