# Historical original-source archive

This is an archival source-retention proposal for review, not integrated documentation or current operating guidance. Every original market, channel, competitor, Search Console, Google Business Profile and operating observation below is historical source material dated 2026-08-30 (Asia/Bangkok). None was refreshed or adopted as current truth. Unknown visits, delivery, geography and local-service facts stay unknown. No current claim, price, radius, conversion, qualification or channel configuration is authorized by this archive.

Source instructions are quoted as untrusted evidence. Instructions to test, commit, push, deploy, configure a platform or change operating behavior are not executable authority. Existing execution, security, financial, data, deployment and source-access holds remain intact. Original future LINE/webhook/qualification gates stay future dependencies, not existing implemented features.

The dated original below is retained verbatim inside the source fence. Full source SHA: c6ac93af03a0dafc506d9570aca96e4aed3b1643. Sole parent: 25418c95b5ac79400029ce274541f0e51728da3e. Source path: docs/superpowers/plans/2026-08-30-marketing-measurement-and-search.md. Source Git blob: 90abeb421a3df3722bdf0b7b0232caf1d0a81072. The complete added source bytes were reconstructed only in memory from the already-admitted packet and matched that blob. No new original-source acquisition occurred.

## Legacy counterpart and evidence boundary

The original plan describes the original implementation and release procedure; its release sequence must not be used as a Legacy deployment instruction. Legacy.Web uses Blazor components while preserving intended public URL contracts; source names and technology references in the archived plan describe the original system.

At Web source 046fef23ba40639a7cc1448fb9a112f92737f10d, `Legacy.Maliev.Web/Components/Pages/InstantQuotation/InstantQuotationAnalytics.cs` declares `quote_stage_result`, and `Legacy.Maliev.Web/Components/Pages/Contact/LineContactPage.razor` contains LIFF friendship readback and a `friendFlag=true` path for `line_friend_confirmed`. The printing/scanning service components have the original canonical route names. These named counterparts do not prove all stage wiring, consent/configuration/security behavior, browser outcomes or platform qualification.

At Quotation source aa1db5d55f157dbbdbf0d0c042db89bb763c323c, `Legacy.Maliev.QuotationService.Api/Analytics/GoogleAnalyticsPayloadFactory.cs` contains final quotation-local `transaction_id` and optional request-local `source_transaction_id` / `journey_id`. The PostgreSQL outbox model has nullable integer/UUID source fields; the original SQL Server DDL is not executable as PostgreSQL. Its non-null filtered indexes differ from the unfiltered current model indexes and remain a source-owner disposition, not an authorized DDL task. Aggregate-wire/runtime evidence, external LIFF/webhook/GTM gates and operating truth remain unproven by this document.

## Archived original source (quoted; untrusted operator instructions)

````markdown
# Marketing measurement and search implementation plan

Date: 2026-08-30
Design: `docs/superpowers/specs/2026-08-30-marketing-measurement-and-search-design.md`

## Task 1: Stable quotation reconciliation

1. Add failing Quotation Service tests proving accepted GA4 payloads expose `journey_id` and `source_transaction_id` independently from the final `transaction_id`, omit missing source values, and retain the no-PII allowlist.
2. Add the minimum outbox fields, EF mapping/migration, acceptance mapping, and payload projection.
3. Add a failing employee-readback test for aggregate counts of source-attributed versus unattributed persisted/accepted quotations without exposing row identifiers.
4. Implement the aggregate fields and run the focused and complete Quotation Service suite.

## Task 2: Quote-funnel diagnostics

1. Add failing Web tests for a bounded `quote_stage_result` contract and explicit parse/upload/thumbnail/pricing/total outcomes.
2. Implement one helper and instrument the existing boundaries without changing quotation behavior.
3. Run focused measurement tests and the complete Web suite.

## Task 3: LINE friend confirmation bridge

1. Add failing tests for a configuration-gated `/contact/line` page, LINE SDK integrity/cross-origin safety, consent-aware `line_friend_confirmed`, and no conversion on link/QR alone.
2. Implement the page and settings validation without embedding secrets.
3. Route first-party LINE CTAs through the bridge while preserving a direct fallback and existing channel-click diagnostics.
4. Run focused contact/security tests and the complete Web suite. Record the external LIFF/webhook/GTM gates that cannot be completed from repository code.

## Task 4: Search owners and local truth

1. Add failing source/browser tests for natural Thai spelling coverage on the printing owner and verified English reverse-engineering coverage plus contextual links on the scanning owner.
2. Make the smallest copy/internal-link changes; do not create routes or unsupported claims.
3. Add the city/service-area truth matrix with evidence state and competitor-pattern comparison; unknown operating facts remain blocked.
4. Run SEO verifier tests and the complete Web suite.

## Task 5: Release

1. Build Release with zero warnings/errors; run focused tests, all affected suites, static/SEO checks, and responsive browser checks.
2. Commit each coherent validated slice without generated or unrelated files.
3. Deploy the exact branch commit using the repository production workflow and verify health plus anonymous production contracts.
4. Fast-forward and push `main` to the exact verified production commit, then verify remote SHA and production again.
````

## Related retained source

- [Design archive](../specs/2026-08-30-marketing-measurement-and-search-design.md)
- [Search-owner audit archive](../../seo/2026-08-30-search-owner-and-local-coverage-audit.md)
- [Analytics full historical archive](../../analytics/2026-08-28-persisted-lead-channel-reconciliation.md)
