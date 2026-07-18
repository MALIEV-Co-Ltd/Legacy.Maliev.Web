# Contact-channel quality measurement implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Legacy.Maliev.Web ready to compare LINE, Messenger, and WhatsApp quality without promoting diagnostic clicks to primary Google Ads conversions.

**Architecture:** Extend the consent-gated contact classifier with the retained approved Messenger destination and expose that link through the shared social surface. Keep the browser payload limited to exact event/channel/destination/context values; join only aggregate GA4 and CRM outcomes in the weekly report. A verified WhatsApp business destination and comparative production result remain post-release gates.

**Tech Stack:** .NET 10 Blazor static SSR, Razor components, browser JavaScript, xUnit, Markdown.

## Global constraints

- `request_quote` remains the only primary/biddable Ads conversion.
- `line_click`, `messenger_click`, and `whatsapp_click` remain secondary/non-biddable diagnostics.
- All browser events flow through `window.malievAnalytics.emit` and the existing consent queue.
- No PII, contact value, query string, UTM, referrer, free text, full URL, session value, or credential may enter the event payload.
- Do not modify issue #22 structured-data/FAQ files or Instant Quotation issues #148-#153.
- Do not publish GTM, mutate Google consoles, deploy, or change GKE.

---

### Task 1: Encode the missing channel contract as failing tests

**Files:**
- Modify: `Legacy.Maliev.Web.Tests/PublicContactChannelAnalyticsMigrationTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/SharedFooterMigrationTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/WebSurfaceTests.cs`
- Create: `Legacy.Maliev.Web.Tests/ChannelQualityMeasurementContractTests.cs`

**Interfaces:**
- Consumes: `SocialNetworks`, `SocialLinks`, `PublicContactChannelAnalytics`, and the channel-quality runbook.
- Produces: exact source/runtime assertions for Messenger plus fail-closed payload, KPI, and decision-policy assertions.

- [x] **Step 1: Require the approved Messenger URL, footer link, exact event/channel/destination tuple, and absence of a generic Facebook click event.**
- [x] **Step 2: Require a runbook with exact LINE/Messenger/WhatsApp payloads, aggregate KPIs, observation gates, and a non-biddable decision rubric.**
- [x] **Step 3: Run the focused tests and verify they fail against current main because Messenger and the dedicated runbook are missing.**

### Task 2: Implement the minimal non-Instant-Quotation readiness boundary

**Files:**
- Modify: `Legacy.Maliev.Web.Application/SocialNetworks.cs`
- Modify: `Legacy.Maliev.Web/Components/Layout/SocialLinks.razor`
- Modify: `Legacy.Maliev.Web/Components/Analytics/PublicContactChannelAnalytics.razor`

**Interfaces:**
- Consumes: retained approved Messenger URL `https://m.me/maliev.manufacturing/`.
- Produces: `messenger_click` with `channel=messenger`, `destination=facebook_messenger`, and the existing controlled page context through `window.malievAnalytics.emit`.

- [x] **Step 1: Add the approved Messenger destination to the application registry and shared social links.**
- [x] **Step 2: Add an exact `m.me` host/path classifier without adding Facebook, PII, URL, or campaign fields.**
- [x] **Step 3: Keep WhatsApp fail closed: recognize only supported hosts with an explicit application-owned `whatsapp_business` marker until a verified business URL is approved.**
- [x] **Step 4: Run focused runtime tests and verify they pass.**

### Task 3: Define measurement readiness and the decision rubric

**Files:**
- Create: `docs/channel-quality-measurement.md`
- Modify: `docs/analytics-validation.md`

**Interfaces:**
- Consumes: the exact browser contract and merged issue #23 analytics boundary.
- Produces: aggregate weekly KPI definitions, data-quality/observation gates, and an owner-reviewed decision policy.

- [x] **Step 1: Define exact event, channel, destination, and context allowlists plus consent and PII prohibitions.**
- [x] **Step 2: Define weekly sessions, clicks, click rate, qualified inquiries, quote requests, wins/losses, Ads cost, and quality-rate formulas by channel.**
- [x] **Step 3: Define readiness gates and require owner review before CTA/sitelink/routing changes; click volume alone can never decide.**
- [x] **Step 4: Record the verified-WhatsApp-destination and post-release production-observation blockers without fabricating results.**
- [x] **Step 5: Run focused tests and verify they pass.**

### Task 4: Validate and publish the coherent slice

**Files:**
- All files listed above only.

**Interfaces:**
- Consumes: the completed implementation, docs, and tests.
- Produces: a merged issue #21 PR, exact-main CI evidence, and an updated Project #2 item without deployment.

- [x] **Step 1: Run Release build, full tests, deterministic browser assets, changed-file format, vulnerability, actionlint, and gitleaks gates.**
- [x] **Step 2: Confirm no issue #22 or Instant Quotation files changed and no WhatsApp number/result was invented.**
- [ ] **Step 3: Commit, push, open a PR closing #21, monitor CI, merge, verify exact-main CI, and update Project #2.**
