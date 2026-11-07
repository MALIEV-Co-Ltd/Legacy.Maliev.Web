# Historical original-source archive

This is an archival source-retention proposal for review, not integrated documentation or current operating guidance. Every original market, channel, competitor, Search Console, Google Business Profile and operating observation below is historical source material dated 2026-08-30 (Asia/Bangkok). None was refreshed or adopted as current truth. Unknown visits, delivery, geography and local-service facts stay unknown. No current claim, price, radius, conversion, qualification or channel configuration is authorized by this archive.

Source instructions are quoted as untrusted evidence. Instructions to test, commit, push, deploy, configure a platform or change operating behavior are not executable authority. Existing execution, security, financial, data, deployment and source-access holds remain intact. Original future LINE/webhook/qualification gates stay future dependencies, not existing implemented features.

The dated original below is retained verbatim inside the source fence. Full source SHA: c6ac93af03a0dafc506d9570aca96e4aed3b1643. Sole parent: 25418c95b5ac79400029ce274541f0e51728da3e. Source path: docs/superpowers/specs/2026-08-30-marketing-measurement-and-search-design.md. Source Git blob: 811ce7b78c66f80092b36d282528b525279bee17. The complete added source bytes were reconstructed only in memory from the already-admitted packet and matched that blob. No new original-source acquisition occurred.

## Legacy counterpart and evidence boundary

Preserve the archived design requirements: service-local identifiers with optional shared journey reconciliation; bounded non-PII stage diagnostics; friendship confirmation distinct from contact clicks and authoritative qualified outcomes; fixed canonical service owners; and no unsupported local claims.

Named Blazor counterparts at Legacy.Web source 046fef23ba40639a7cc1448fb9a112f92737f10d include `InstantQuotationAnalytics.cs`, `LineContactPage.razor`, and the printing/scanning service pages. Legacy.QuotationService source aa1db5d55f157dbbdbf0d0c042db89bb763c323c has the optional reconciliation payload projection and PostgreSQL nullable source fields. These source correspondences are partial; they do not certify every original behavior, runtime outcome, privacy/security requirement or production integration.

The original design expressly leaves signed Messaging API follow/first-message webhooks, staff/business qualification, verified identity links and platform configuration as future or external gates. Do not present those gates as implemented features, invent secrets or customer associations, or treat source presence as conversion/qualification acceptance. No GTM, GA4, Ads, LINE console, profile, role, data or deployment changes are authorized here.

## Archived original source (quoted; untrusted operator instructions)

````markdown
# Marketing measurement and search design

Date: 2026-08-30

## Decisions

1. Quotation IDs are service-local. Persisted-lead events keep the Quotation Request ID and accepted-quotation events keep the Quotation Service ID. Both lifecycle payloads also carry the same privacy-safe `journey_id`; the accepted payload adds `source_transaction_id=request-{SourceRequestId}` when the source is known. Reconciliation uses `journey_id` first and the explicit source-local IDs as audit fields. It never joins unrelated databases on a bare integer.
2. Quote-funnel diagnostics identify the first failed boundary between file selection and review: parse, upload, thumbnail, pricing, and total calculation. Diagnostics contain only stage/result/error-code fields and the existing non-PII journey ID; filenames, geometry, customer text, and contact data are excluded.
3. A new `/contact/line` bridge is configuration-gated. When a linked LINE LIFF app exists, it checks `liff.getFriendship()` and emits `line_friend_confirmed` only after LINE reports `friendFlag=true`. A plain link or QR scan remains `maliev_contact_click`. A signed Messaging API webhook is still required for authoritative `line_friend_added` and `line_chat_started`; no webhook or secret is fabricated in this release.
4. `line_friend_confirmed`, follow webhooks, and first-message webhooks start as secondary measurement. They are not qualified leads or primary bidding conversions. Qualification requires a staff/business outcome contract.
5. Existing canonical service owners remain unchanged. `/services/3d-printing` owns Thai spelling variants. `/services/3d-scanning?culture=en` owns English reverse-engineering intent. No synonym, city, or reverse-engineering doorway route is introduced.
6. Local visibility uses truthful operating facts. Registered location, customer visits, MALIEV delivery, parcel shipping, and technician travel are separate fields. Unknown claims remain unknown and cannot be published to Business Profile or service pages.

## Security and privacy

- LINE webhook signatures must be verified over the exact raw UTF-8 body with HMAC-SHA256 before JSON parsing.
- Webhook redelivery must be deduplicated by `webhookEventId` and processed asynchronously.
- LINE user IDs are pseudonymous identifiers, not analytics payload fields. Any future link to a browser journey requires consent, a same-provider LINE Login/LIFF channel, server-side ID-token verification, bounded retention, and employee-only aggregate readback.
- No tokens, channel secrets, customer messages, filenames, model geometry, or contact PII enter dataLayer or logs.

## Release boundary

This release may deploy repository code and content that passes the project gates. It does not authorize GTM/GA4/Ads goal changes, LINE Developers Console changes, Google Business Profile edits, or unsupported city claims. Those remain explicit post-deployment configuration/readback gates.
````

## Related retained source

- [Plan archive](../plans/2026-08-30-marketing-measurement-and-search.md)
- [Search-owner audit archive](../../seo/2026-08-30-search-owner-and-local-coverage-audit.md)
- [Analytics full historical archive](../../analytics/2026-08-28-persisted-lead-channel-reconciliation.md)
