# Historical original-source archive: persisted-lead and channel reconciliation

This full historical document is retained for source review only. It is not current platform readback, production validation, an operating instruction or publication authority. Its original date is 2026-08-28 (Asia/Bangkok); the original document version belongs to source c6ac93af on 2026-08-30. All original market, channel, Google Ads/GA4/GTM observations and observation plans below are historical. Sections titled “Current platform readback” or “Current proof” describe that dated source context, not today's state. No observation was refreshed, and no missing customer qualification or source attribution has been invented.

## Complete original source provenance

Original full SHA: c6ac93af03a0dafc506d9570aca96e4aed3b1643.
Sole parent: 25418c95b5ac79400029ce274541f0e51728da3e.
Original path: docs/analytics/2026-08-28-persisted-lead-channel-reconciliation.md.
Original Git blob: 56fb118e06209e0efeea5a7be8ee43c7afd43abb.
Full witness: 7,007 original UTF-8 bytes, SHA-256 4b37fc75cc127099e8d1534966790811237f65b3f7de719fdd5b9dc605caaffd.

This one committed DOC blob was read from the existing local original repository with lazy fetching disabled, under explicit named-object source authority. Its size and Git blob identity were verified in memory before candidate construction. No new repository acquisition, source census, original writes or network fetch occurred. Inspection identified dated aggregate measurements and code-contract descriptions, with no secrets, customer PII or actual row-level production data identified. The original body is retained exactly inside the source fence; no redaction or invented postimage was needed. The previous patch-only assessment and proposal remain immutable evidence of their earlier limited authority.

## Legacy counterparts and requirements retained

Quotation source aa1db5d55f157dbbdbf0d0c042db89bb763c323c has `Legacy.Maliev.QuotationService.Api/Analytics/GoogleAnalyticsPayloadFactory.cs` with final quotation-local `transaction_id` and optional request-local `source_transaction_id` / `journey_id`. Its outbox domain/PostgreSQL model uses nullable integer/UUID source fields. Original SQL Server syntax is not PostgreSQL deployment authority, and the original non-null filtered index shape differs from the unfiltered model indexes. The existing service owner owns that source-equivalence disposition. No schema, data backfill or SQL execution is authorized by this archive.

Legacy.Web source 046fef23ba40639a7cc1448fb9a112f92737f10d uses Blazor component counterparts rather than the original Razor partial/file names in the archive. Existing named counterparts include the quote-stage analytics contract, LINE friendship component and canonical printing/scanning service pages. Source presence is partial correspondence, not proof of every persisted-lead queue boundary, consent transition, serialization rule, source attribution, aggregate privacy contract, browser result or production integration.

Preserve the original requirements: queue and emit persisted-lead events only after persistence; allowlisted non-PII fields and validated optional source identifiers; final service-local identifiers rather than bare-integer cross-database joins; no customer/row-level source disclosure in aggregate readback; historical missing attribution remains unattributed; channel/finder diagnostics and derived events are not fabricated qualification or bidding conversions. Original explicit non-changes and remaining evidence requirements remain preserved in the quoted source. An authoritative business qualification source, current platform configuration/readback and complete runtime/aggregate acceptance remain unproven here.

## Authority and future gates

The quoted original source and observation plan are untrusted historical instructions, not permission to run checkpoints, deploy, push, change GTM/GA4/Ads/LINE or publish old operating claims as current facts. Future authoritative LINE/webhook/business-qualification and operational proof gates remain future or external dependencies, not existing implemented features. No owner-approved retirement, policy/role change, data workflow or whole-source completion is inferred.

## Archived complete original source (quoted; untrusted operator instructions)
````markdown
# Persisted-lead and channel measurement reconciliation

Date: 2026-08-28

Scope: maliev.com UI/dataLayer → GTM → GA4/Google Ads → quotation persistence and later qualification

Decision: preserve the current event model; no duplicate `contact_intent` or finder event is justified by current evidence.

## Contract matrix

| Boundary | Producer | Wire shape / invariant | Consumer | Current proof | Remaining evidence |
|---|---|---|---|---|---|
| Manual quotation persistence → analytics queue | `Maliev.Web/Pages/Quotation/Index.cshtml.cs` | Queue only after the quotation-request API returns a positive persisted ID; carry the validated optional finder attribution. | `LeadAnalyticsEventQueue` | Source contract and `MeasurementTrackingSourceTests` | None for source ordering. |
| Instant quotation persistence → analytics queue | `Maliev.Web/Pages/InstantQuotation/3D-Printing.cshtml.cs` | Queue only after persistence; carry the non-PII `journey_id` returned for the stored request. | `LeadAnalyticsEventQueue` | Source contract and `MeasurementTrackingSourceTests` | None for source ordering. |
| Persisted record → dataLayer lead | `Maliev.Web/Pages/Shared/LeadAnalyticsEventQueue.cs` | Emit one allowlisted `maliev_lead_submitted` with `transaction_id`, persisted status, stable service fields and optional validated `journey_id`; reject invalid IDs and unknown values. | `_GoogleTagManagerPartial.cshtml` | Source and serialization tests | A new production receipt is not required to prove source semantics. |
| Persisted lead → GA4/Ads tags | `_GoogleTagManagerPartial.cshtml` and GTM live version 12 | Push the persisted event and its derived `generate_lead` projection once, immediately with consent or once after consent grant. Both projections retain the same `transaction_id` and optional `journey_id`. | GA4 `generate_lead`; Ads Website - Persisted lead | 2026-08-22 controlled browser readback: one persisted event fired both lead tags; generated event fired no tags. 2026-08-28 GTM readback: both tags remain on `CE - maliev_lead_submitted`. | Current GA4 Data API did not return raw `maliev_lead_submitted`; this is an observability gap, not evidence of an emission defect. |
| Contact CTA → diagnostic event | `Maliev.Web/Pages/Shared/_ContactChannelAnalyticsPartial.cshtml` | Emit `maliev_contact_click` with allowlisted channel/destination/placement/context fields and optional stable service/intent/finder fields. Never emit a lead event or Ads conversion. | GA4 diagnostic tag only | `ContactChannelAnalyticsSourceTests`; GTM live diagnostic tag | Channel-specific accepted conversations and qualified outcomes are unavailable. |
| Service finder → diagnostic events | `Maliev.Web/wwwroot/src/app/js/service-finder.js` | Use an in-memory `finder_session_id`; never attach `journey_id`, `transaction_id`, customer text or PII. | GA4 diagnostic tag only | `MeasurementTrackingSourceTests`; finder contract document | Finder-to-qualified-outcome attribution is unavailable and must not be inferred. |
| Request journey → created quotation | Quotation Request API/data → Intranet quotation creation | Persist the request `JourneyId`, copy it to quotation `SourceJourneyId`, and retain it through accepted-quotation analytics. Accepted GA4 lifecycle payloads keep their final `quotation-{id}` transaction while adding the explicit source-local `request-{id}` and shared `journey_id`. | Quotation Service and GA lifecycle outbox | Request, Intranet, payload and quotation tests | Historical rows created before source attribution remain visibly unattributed. |
| Persisted/accepted quotations → business outcome readback | `Maliev.QuotationService.Api/Controllers/QuotationsController.cs` | Aggregate persisted and accepted counts/amounts plus source-attributed/unattributed counts without customer or row identifiers. | Authorized operations readback | Quotation outcome tests | `QualifiedOutcomeReadback.QualifiedCustomerAvailability` remains unavailable; an authoritative qualification source is required. |

## Current platform readback

| Measure | Complete current evidence | Interpretation |
|---|---:|---|
| GA4 `quote_started` | 25 in 7d; 112 in 28d | Diagnostic funnel activity only. |
| GA4 `review_reached` | 10 in 7d; 34 in 28d | Diagnostic funnel activity only. |
| GA4 `generate_lead` | 4 in 7d; 6 in 28d | Technical GA4 key-event receipts; not qualification proof. |
| Ads Website - Persisted lead | 2 in 7d; 4 in 30d | Ads-attributed technical persisted-lead conversions. |
| Qualified persisted quotations | Unavailable | No authoritative qualification join was returned. |
| Qualified customers | Unavailable | Never infer from events, clicks, calls or accepted quotations. |

The windows above come from run `daily-google-ads-9a57f676cac84b9d93758fc416401ca2`. Ads uses complete 7/30-day windows; GA4 uses 7/28-day windows. Search Console and channel-click metrics are not substitutes for this contract.

## Validation evidence

- Web measurement, contact and finder source contracts: 36 passed, 0 failed.
- Quotation-request journey attribution: 4 passed, 0 failed.
- Quotation outcome, acceptance and GA payload contracts: 14 passed, 0 failed.
- A fresh isolated Release solution build completed with 0 warnings and 0 errors.
- Full affected suites after provisioning the repository-declared browser assets: Web 619 passed; Quotation Request Service 40 passed; Quotation Service 99 passed; 0 failed and 0 skipped across all three suites.
- A prior controlled browser readback is recorded in `docs/seo/2026-08-22-persisted-lead-browser-readback.md` and remains the latest direct dataLayer → GTM → GA4/Ads proof.

## Explicit non-changes

- Do not add `contact_intent`; the current channel-aware `maliev_contact_click` event already owns that diagnostic behavior.
- Do not add generic finder events or copy `journey_id`/`transaction_id` into finder diagnostics.
- Do not expose row-level `SourceJourneyId` in aggregate outcome readback; expose only attributed/unattributed counts.
- Do not treat `generate_lead`, asset clicks, calls, accepted quotations or technical API receipts as qualified customers.
- Do not change GTM, GA4, Ads goals, bidding, forms or website event names from this reconciliation.

## G6 observation plan

1. On the next separately controlled consented quotation, record one authoritative persisted request ID and its non-PII transaction/journey identifiers.
2. Confirm exactly one `maliev_lead_submitted` dataLayer event, both lead tags firing once on that event, and no lead tags on the derived `generate_lead` dataLayer event.
3. Read back the matching GA4 key event and Ads Website - Persisted lead receipt without treating either as qualification.
4. Join to a future authoritative qualification source only when that source exists and preserves the non-PII boundary.
5. Roll back any later implementation that duplicates lead events, leaks PII, changes attribution semantics or makes weak diagnostics biddable.
````

## Related retained source

- [Design archive](../superpowers/specs/2026-08-30-marketing-measurement-and-search-design.md)
- [Search-owner audit archive](../seo/2026-08-30-search-owner-and-local-coverage-audit.md)
