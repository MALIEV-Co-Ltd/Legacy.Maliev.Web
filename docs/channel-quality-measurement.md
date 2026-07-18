# LINE, Messenger, and WhatsApp quality measurement

This runbook makes the legacy website measurement-ready for issue #21. It is subordinate to the
consent, conversion, malware, and publication boundaries in
[`analytics-validation.md`](./analytics-validation.md). It does not publish GTM, change a Google
Ads goal, authorize deployment, or claim that one contact channel performs better than another.

## Exact browser contract

Every direct-contact click is a secondary/non-biddable diagnostic. `request_quote` remains the
only primary Google Ads conversion. The browser emits exactly four fields:

| Event | `channel` | `destination` | Approved destination and state |
| --- | --- | --- | --- |
| `line_click` | `line` | `line_oa` | `https://line.me/ti/p/@maliev`; active and visually primary before Messenger |
| `messenger_click` | `messenger` | `facebook_messenger` | `https://m.me/maliev.manufacturing/`; active as a secondary footer diagnostic |
| `whatsapp_click` | `whatsapp` | `whatsapp_business` | Inactive until the owner verifies the WhatsApp Business destination; supported hosts also require the application-owned `data-maliev-contact-destination="whatsapp_business"` marker |

The fourth field is `context`, with exactly one of `contact`, `quotation`, `instant_quotation`,
`services`, `about`, `career`, `legal`, `member`, `knowledge`, or `other`. The event name stays
channel-specific; a Messenger click is never a generic Facebook click or an inbound Facebook
referral.

The payload must not contain `service`, `locale`, campaign, UTM, referrer, link URL, query string,
telephone/recipient value, DOM text, name, email, message, user/account ID, access/refresh token,
cookie/session value, or any other free-form field. Locale, service, and landing-page dimensions
may be derived by GTM/GA4 after consent as described in the parent analytics contract; they are not
browser event fields.

## Consent and privacy gate

All three events use `window.malievAnalytics.emit`. While Consent Mode defaults are denied, the
application may hold an event only in its in-memory queue and GTM remains unloaded. Accepting
optional tracking updates all four consent signals, flushes the queue exactly once, and then loads
GTM. Rejecting or revoking consent sets all four signals to denied, clears the queue, and never
loads GTM.

No analytics emission associated with a provider link, non-JavaScript navigation, or server
fallback may bypass this gate. The destination navigation itself remains available without
optional tracking. A channel click must never fire `request_quote`, an Ads conversion tag, a GA4
key event, or a provider-side conversation/lead event merely because navigation occurred.

## Weekly aggregate KPI contract

Use complete Monday-Sunday weeks in `Asia/Bangkok`. The comparison grain is week x channel, with
controlled reporting-only breakdowns for locale, service, and landing-page type when available.
Do not export people, conversations, messages, phone numbers, provider identifiers, or raw URLs.

| KPI | Definition |
| --- | --- |
| `eligible_sessions` | GA4 `Sessions` for consented sessions containing at least one `page_view` whose canonical path is in the archived `/sitemap.xml` route inventory for the deployed SHA and whose shared footer contract rendered that active channel; deduplicate at session scope and keep this blank for an inactive channel |
| `channel_clicks` | Count of the exact secondary click event for the channel |
| `click_rate` | `channel_clicks / eligible_sessions`; instrumentation diagnostic only |
| `started_conversations` | Aggregate provider/CRM count where the provider supplies a trustworthy channel count; otherwise blank |
| `qualified_inquiries` | Aggregate CRM/manual outcomes tagged with the controlled channel after a real business qualification |
| `qualified_inquiry_rate` | `qualified_inquiries / started_conversations`; leave blank rather than substituting clicks when conversation counts are unavailable |
| `quote_requests` | Aggregate persisted quote requests attributed to the controlled channel through approved CRM/manual tagging |
| `quote_request_rate` | `quote_requests / qualified_inquiries`; blank when the denominator is zero |
| `won_leads` | Aggregate qualified opportunities marked won |
| `lost_leads` | Aggregate qualified opportunities marked lost |
| `win_rate` | `won_leads / (won_leads + lost_leads)`; blank until an outcome is decided |
| `ads_cost` | `unavailable` in this release: no reviewed acquisition-to-qualified-inquiry cost allocation exists, and campaign cost must not be duplicated or guessed across contact channels |
| `cost_per_qualified_inquiry` | `unavailable` while `ads_cost` is unavailable; a future version must define and validate one deterministic allocation before either field is populated |

`ads_cost` is `unavailable` for the current readiness contract, so no channel cost or
cost-per-qualified-inquiry value may appear in the comparison yet.

At release, archive the deployed SHA's `/sitemap.xml` and the shared-footer channel list with the
weekly evidence. Build the GA4 session-scoped segment from that immutable route inventory; do not
substitute all-site sessions or clickers for `eligible_sessions`. Reconcile `channel_clicks` to
GTM Preview/GA4, provider conversations to provider exports, and qualified/quote/win outcomes to
the CRM or the owner's controlled manual ledger. Never infer a qualified inquiry, quote request,
win, or channel cost from a click.

## Observation-readiness gate

The channel comparison is not decision-ready until all of these conditions hold:

1. GTM Preview and GA4 DebugView prove the exact payload, consent ordering, one-click/one-event
   behavior, English/Thai parity, and absence of PII for every active channel.
2. A verified WhatsApp Business destination is approved and rendered before WhatsApp is included;
   until then WhatsApp remains `not_active`, not zero-performing.
3. At least eight complete weeks exist after the same instrumentation version reached production.
4. Each channel being compared has at least 20 qualified inquiries and at least 10 decided
   won/lost outcomes; sparse channels remain `insufficient_evidence`.
5. At least 90% of qualified inquiries have one controlled channel value, and weekly reconciliation
   has no duplicate events, unknown values, or unresolved provider/CRM discrepancies.

Changing instrumentation, channel visibility, CRM tagging, or campaign routing restarts the
observation window for the affected comparison. Missing provider metrics stay blank and must not
be replaced with click counts.

## Channel decision rubric

1. Before the observation-readiness gate passes, preserve the owner policy: LINE stays the primary
   visible Thai contact route, Messenger stays secondary, and WhatsApp stays hidden until verified.
2. Click volume alone never changes CTA order, sitelinks, campaign routing, or bidding goals.
3. After readiness, compare `qualified_inquiry_rate`, `quote_request_rate`, `win_rate`, and
   `cost_per_qualified_inquiry` together. A click-rate advantage is diagnostic, not quality proof.
4. Recommend deprioritization only when a channel underperforms on qualified-inquiry quality and
   cost for four consecutive complete weeks, has no offsetting win-rate advantage, and the data
   quality gate remains satisfied.
5. Every CTA, sitelink, or routing change requires owner approval and a recorded effective date;
   measure it as a new observation window. No rule automatically mutates the site or ad account.
6. `line_click`, `messenger_click`, and `whatsapp_click` always remain secondary/non-biddable.
   Only a separately approved future decision backed by qualified-lead evidence could revisit
   classification, and this runbook does not grant that approval.

## Pending production evidence

No comparative production result exists yet. The application has not been cut over, the GTM/GA4
configuration has not been owner-verified against this release, and there is no approved WhatsApp
Business destination in the repository. Report the state as `measurement_ready` for LINE and
Messenger after release-candidate QA, `not_active` for WhatsApp, and
`production_observation_pending` for the quality comparison. Do not backfill or fabricate results.

After owner-approved release, attach the following weekly evidence to Project #2 without PII:

- deployed application SHA and GTM container version;
- GTM Preview and GA4 DebugView evidence for each active channel;
- aggregate KPI export and reconciliation notes;
- observation-gate status and sample-size counts;
- owner decision, effective date, and rollback instruction when a presentation/routing change is
  approved.
