# Legacy Web analytics and conversion validation

This runbook defines the production contract for the public legacy website. It applies to the
`Legacy.Maliev.Web` image deployed in the existing `maliev-legacy` namespace and to Google Tag
Manager container `GTM-KHDDLVRR`. It does not authorize a deployment or a Google container publish
by itself.

## Ownership and data-flow boundary

The browser application owns the non-PII event names and controlled payloads in this document.
GTM owns consent-aware routing from those events to GA4 and Google Ads. GA4 owns behavioral and
acquisition reporting. Google Ads owns conversion classification and bidding eligibility. Search
Console and Google Business Profile own aggregate search and Maps signals; neither product is a
browser data-layer destination and neither may be represented as an individual conversion.

The mandatory sequence is:

1. On every page load, queue Consent Mode v2 defaults for `ad_storage`, `analytics_storage`,
   `ad_user_data`, and `ad_personalization` as `denied`, with `wait_for_update` set to `500` ms.
2. Do not load GTM and do not emit measurement events to `dataLayer` while consent is denied.
   Application events may wait only in the application-owned in-memory queue.
3. On accept, queue the consent update with all four signals set to `granted`, update the
   application consent state, flush each queued event exactly once, and then load GTM, in that
   order.
4. On reject or revoke, queue all four signals as `denied`, clear the in-memory queue, and do not
   invoke the GTM loader. Rejecting or revoking consent clears the in-memory queue.

No event is discarded solely because consent is initially denied; it remains application-local
until the visitor accepts or explicitly rejects/revokes consent on that page. Events must not add
a synthetic consent parameter. GA4 requires `analytics_storage`; Ads requires `ad_storage` and
`ad_user_data`; `ad_personalization` remains denied unless it was explicitly granted. There is no
non-JavaScript fallback conversion.

## Conversion contract

| Event | Contract state | Purpose | Ads classification |
| --- | --- | --- | --- |
| `request_quote` | Stable | Backend-persisted contact or quotation request | Primary Ads conversion |
| `file_upload_start` | Stable | A positive attachment count enters the verified submission path | Secondary only |
| `file_upload_complete` | Stable | Every attachment was scanned, stored, and linked to the persisted request | Secondary only |
| `phone_click` | Stable | Business telephone click | Secondary only |
| `email_click` | Stable | Business email click | Secondary only |
| `line_click` | Stable | LINE Official Account click | Secondary only |
| `messenger_click` | Stable when link exists | Facebook Messenger direct-contact click | Secondary only |
| `whatsapp_click` | Stable when link exists | WhatsApp Business click | Secondary only |
| `instagram_click` | Stable | Instagram profile click | Secondary only |
| `youtube_click` | Stable | YouTube channel click | Secondary only |
| `maliev_review_link_click` | Stable | Google Business Profile review-link click | Secondary only |
| `google_maps_click` | Reserved, not emitted yet | Google Maps directions/profile click | Secondary only |
| `tiktok_click` | Reserved, not emitted yet | TikTok profile click | Secondary only |
| `threads_click` | Reserved, not emitted yet | Threads profile click | Secondary only |

`request_quote` remains the only primary event in this table that may be a Google Ads bidding
conversion. Page views, service/product views, add-to-cart events, social clicks, file events,
Google-hosted lead form submissions, LINE, Messenger, WhatsApp, telephone, and email clicks must
not be primary conversions.

LINE is the primary visible contact CTA on high-intent Thai pages, while `messenger_click` and
`whatsapp_click` remain separate secondary/non-biddable direct-contact diagnostics under issue
#21. A Messenger outbound click is distinct from a Facebook inbound referral: Facebook may appear
as an inbound `lead_source`, but this contract does not define a generic `facebook_click` event.
The Instant Quotation names `upload_failure`, `estimate_shown`, and
`review_reached` are pending under issue #152; their fail-closed maximum keys and rules below are
frozen for validation, but the events remain inactive until #152 tests and #153 review pass. PR
#156 commit `e7e6783bab2d3aa577f5e11b06a6316141663763` is validation evidence for the stable event
and consent boundary, not authorization to publish tags or deploy.

The `request_quote` data-layer payload is non-PII and contains only:

- `event`: always `request_quote`;
- `intent_type`: `contact_request` or `quotation_request`;
- `service`: `general_contact`, `3d_printing`, `3d_scanning`, `cnc_machining`,
  `injection_molding`, or `custom_manufacturing`;
- `transaction_id`: an opaque persisted reference such as `message-913` or `quotation-713`;
- `submission_status`: always `persisted`;
- `has_files` and `file_upload_completed`: booleans.

`file_upload_start` contains exactly `event`, a controlled `service`, and a positive `file_count`.
`file_upload_complete` contains exactly `event`, the same controlled `service`, and the persisted
`transaction_id`; it is emitted only after every file is scanned, stored, and linked. A rejected,
failed, partial, or abandoned upload never emits `file_upload_complete`.

File and pending Instant Quotation events allow only `3d_printing`, `3d_scanning`,
`cnc_machining`, `injection_molding`, or `custom_manufacturing` as `service`. Instant 3D quotation
uses `service=3d_printing`; its successful primary event uses
`intent_type=quotation_request` and `submission_status=persisted`.

The persisted event must appear exactly once after persistence and never for validation failures,
reCAPTCHA rejection, downstream failure, page view, or UI-only progress. GTM and Ads use
`transaction_id` for deduplication. English and Thai routes use identical names, parameters, and
controlled values.

The three stable payloads forbid `locale`, `landing_page_type`, `quote_flow_step`, `part_count`,
`lead_source`, `source`, `medium`, `campaign`, referrer, UTM fields, full URLs, `currency`, `value`,
`contact_channel`, synthetic consent fields, PII, session data, upload/storage details, and
credentials. Reporting enrichment happens after consent outside the application payload.

Pending Instant Quotation events remain inactive and fail-closed. Their maximum proposed keys are
`upload_failure` (`event`, `service`, `failure_category`, `file_count`), `estimate_shown` (`event`,
`service`), and `review_reached` (`event`, `service`). `failure_category` must be a finite non-PII
enum: `validation`, `authorization`, `conflict`, `dependency_unavailable`, or `unexpected`. Map
unsupported/missing extensions, empty/oversized/malformed files, and typed 413/415/422 outcomes to
`validation`; 401/403 to `authorization`; 409, idempotency, or session conflicts to `conflict`;
adapter unavailability, typed 503, dependency timeouts, and dependency transport failures to
`dependency_unavailable`; and remaining typed failures to `unexpected`. Never expose raw text,
codes, status/body content, exceptions, filenames, file metadata, or internal identifiers.

The proposed fail-closed firing rules are also pending: `upload_failure` fires once per independent
file attempt with `file_count=1`, does not fire for cancel/stale/cleanup, and treats a retry as a new
attempt; `estimate_shown` fires once per complete authoritative visible estimate revision, never an
advisory/partial/error result; `review_reached` fires the first time an authoritative revision
enters Review and unchanged navigation does not refire it. Internal attempt/revision identifiers
are deduplication state only and are never emitted. These events remain inactive until issue #152
tests prove unknown fields/categories fail, identifiers never emit, and the firing/deduplication
rules pass independent issue #153 review.

Do not add names, email addresses, telephone numbers, company names, message text, filenames,
full URLs, user IDs, or authentication/session values to any analytics payload.

## GA4 parameter and custom-dimension registry

Register all six entries as event-scoped GA4 custom dimensions. They are reporting enrichments
derived by GTM/GA4 after consent and are not additional application event fields. In particular,
`locale`, `landing_page_type`, `quote_flow_step`, and `lead_source` are forbidden in the three
stable application event payloads. GTM must never forward raw query strings, referrers, UTM
values, or full URLs as custom-dimension values.

| Custom dimension | Controlled values and derivation |
| --- | --- |
| `service` | `general_contact`, `3d_printing`, `3d_scanning`, `cnc_machining`, `injection_molding`, or `custom_manufacturing`; use the event value when present, otherwise derive only from a recognized service route |
| `locale` | `en` or `th`, derived from the rendered document language; never infer from a person's location |
| `contact_channel` | `phone`, `email`, `line`, `messenger`, `whatsapp`, `instagram`, `tiktok`, `youtube`, `threads`, `google_maps`, or `google_business_profile_review`; set only for the matching diagnostic event |
| `lead_source` | A controlled reporting-only acquisition bucket derived from consented GA4 acquisition context; its exact source/medium mapping remains unapproved, so do not emit it from the application or publish a GTM lookup until separately reviewed |
| `landing_page_type` | `home`, `service`, `contact`, `quotation`, `instant_quotation`, `knowledge`, `about`, `career`, `legal`, `member`, or `other`; derive from the normalized route class, not the full URL |
| `quote_flow_step` | `file_upload_started` for `file_upload_start`, `files_linked` for `file_upload_complete`, or `request_persisted` for `request_quote`; do not add pending Instant Quotation steps until issue #152 approves them |

GA4's native `source / medium`, campaign, landing-page, and page-location dimensions remain the
source of exact acquisition reporting. These six controlled dimensions are analysis facets, not
replacements for GA4's native fields and not application payload requirements unless the event
registry above already defines them.

## Outbound channel and referral attribution

- Site-owned outbound clicks use the exact click events above. All are diagnostic GA4 events and
  secondary/non-biddable Ads outcomes.
- Google Maps uses reserved `google_maps_click` with `contact_channel=google_maps`; Google Business
  Profile review links keep `maliev_review_link_click` with
  `contact_channel=google_business_profile_review`. Business Profile views, calls, direction
  requests, and website clicks remain aggregate Business Profile metrics.
- Instagram, TikTok, YouTube, Threads, LINE, Messenger, and WhatsApp outbound links use their
  canonical click event once application instrumentation exists. Messenger direct-contact traffic
  uses `messenger_click`; Facebook referral traffic uses native GA4 acquisition plus the controlled
  reporting bucket and must not be collapsed into the Messenger click event.
- Inbound social and Maps traffic is reported through GA4's native `source / medium` plus the
  controlled `lead_source` bucket. Do not treat an outbound click as a lead, and do not join a
  Search Console or Business Profile aggregate to an individual browser event.
- Campaign links owned by MALIEV should use stable lowercase UTM naming in the source platform,
  but raw UTM values, raw referrers, and full landing URLs must not enter the application data
  layer. Exact campaign taxonomy requires a separate approved media plan.

## Weekly export contract

Produce one weekly, aggregate-only export in the reporting timezone `Asia/Bangkok`. The grain is
ISO week x `service` x `contact_channel` x `locale` x `landing_page_type` x controlled
`lead_source`. Suppress or label unknown dimensions rather than copying raw URLs, campaign names,
queries, or referrers into the export.

The required columns are:

| Column | Source and rule |
| --- | --- |
| `week_start` | Monday date in `Asia/Bangkok` |
| `service` | Controlled GA4 custom dimension |
| `contact_channel` | Controlled GA4 custom dimension; use `none` when no diagnostic channel click occurred |
| `locale` | `en` or `th` |
| `landing_page_type` | Controlled route category, never a full URL |
| `lead_source` | Controlled reporting bucket after its mapping is approved; until then export `unclassified` and retain native GA4 `source / medium` only inside the access-controlled GA4 report |
| `sessions` | GA4 sessions for the aggregate grain |
| `engaged_sessions` | GA4 engaged sessions for the aggregate grain |
| `channel_clicks` | Count of secondary contact, social, Maps, or review click diagnostics |
| `upload_starts` | Count of `file_upload_start` |
| `upload_completions` | Count of `file_upload_complete` |
| `persisted_quote_requests` | Deduplicated count of `request_quote` by `transaction_id` |
| `ads_primary_conversions` | Google Ads primary conversion count; reconcile to persisted quote requests and explain attribution/processing differences |
| `search_clicks` | Aggregate Search Console clicks for the matching week and canonical landing-page group; never user-level joined |
| `search_impressions` | Aggregate Search Console impressions for the matching week and canonical landing-page group |
| `maps_actions` | Aggregate Google Business Profile website, call, and direction actions where the API/UI provides them; never inferred from browser clicks |
| `qualified_inquiries` | Aggregate CRM/manual qualified-lead count by contact channel when available; never infer qualification from a click |
| `won_leads` | Aggregate CRM/manual won outcome count by contact channel when available |
| `lost_leads` | Aggregate CRM/manual lost outcome count by contact channel when available |
| `ads_cost` | Aggregate Google Ads cost allocated by the approved reporting join, never a browser event field |
| `cost_per_qualified_lead` | `ads_cost / qualified_inquiries`; leave blank when no qualified inquiries exist |

The weekly review compares services, channels, and locales without promoting click-only behavior.
Flag duplicate transaction IDs, completion counts greater than upload starts, Ads primary counts
without persisted requests, and any row containing PII or an uncontrolled dimension value.

## Cross-console QA evidence matrix

| Surface | Release-candidate check | Required evidence | Failure gate |
| --- | --- | --- | --- |
| GTM Preview / Tag Assistant | Verify denied defaults, no loader before grant, accept update → flush once → load order, reject/revoke deny → clear → no load, exact event names, and no PII | Preview session URL or screenshots, event payload export, container workspace/version ID | Any early tag, duplicate event, unknown tag/domain, Custom HTML without review, or malware warning blocks publication |
| GA4 DebugView | Verify English/Thai parity, custom dimensions, one persisted `request_quote`, stable upload events, and secondary click classification | Timestamped DebugView screenshots plus test transaction IDs | Missing/duplicate primary event, non-key click/upload event promoted to key, uncontrolled value, or PII blocks approval |
| Google Ads diagnostics | Verify only persisted `request_quote` is primary/account-default and deduplicates by `transaction_id` | Conversion action ID/name, status, goal classification, Tag Assistant diagnostics | Click/upload/page-view primary action, consent warning, duplicate, or unverified tag blocks Ads approval |
| Search Console URL Inspection | Inspect the canonical English and Thai home, service, contact, quotation, and knowledge samples; verify selected canonical, indexability, structured data, and rendered verification meta | Exported inspection result or screenshots for each sample URL | Security Issue, manual action, blocked canonical, unexpected noindex, or malware result blocks release |
| Google Business Profile / Maps | Confirm website/Maps destinations, campaign tagging policy, and available aggregate actions; compare to GA4 without user-level joins | Profile screenshot/export and destination-link inventory | Wrong destination, uncontrolled redirect, PII export, or treating aggregate Maps actions as Ads conversions blocks sign-off |
| Safe Browsing | Check production and release-candidate public hosts before GTM publication | Timestamped clean status URL/screenshot | Suspected malware or phishing blocks both GTM publication and cutover |

Search Console verification is an ownership signal, not analytics consent. The application must
continue rendering the configured `google-site-verification` meta on canonical public pages, but
the verification token must never be copied into events or reports.

## GTM configuration

1. Create Data Layer Variables using data-layer version 2 for `intent_type`, `service`,
   `transaction_id`, `submission_status`, `has_files`, `file_upload_completed`, `channel`,
   `destination`, `context`, and `file_count`.
2. Create one Custom Event trigger named `CE - request_quote - persisted` with event name
   `request_quote` and the condition `submission_status equals persisted`.
3. Create one Custom Event trigger for each stable secondary event listed above. Do not create
   triggers for reserved or pending events, and do not combine a click trigger with the primary
   trigger.
4. Configure the GA4 event tag to preserve the exact event name and controlled parameters. Set
   `transaction_id` as the deduplication identifier for the persisted request.
5. Configure the Google Ads website conversion tag to fire only on
   `CE - request_quote - persisted`. Pass `transaction_id` as the transaction ID. Do not attach an
   Ads conversion tag to a page-view, history-change, link-click, or Google-hosted form trigger.
6. Require `analytics_storage` for GA4. Require `ad_storage` and `ad_user_data` for Google Ads.
   Keep `ad_personalization` denied unless the visitor grants consent. Do not override the consent
   defaults established by the application.
7. Use supported GTM templates. Any Custom HTML tag or third-party script needs a recorded owner,
   destination domain, purpose, and malware review before publication.

## GTM Preview and localized regression

Run this checklist in GTM Preview against the release candidate URL:

1. Clear site data. Open the English quotation route with
   `?culture=en&item=cnc-machining`, submit a valid request, and do not accept optional cookies.
   Confirm no `request_quote`, upload, or contact-click event reaches `dataLayer` or fires a tag.
2. Repeat and accept optional cookies on the result page. Confirm the queued `request_quote` event
   appears exactly once after the consent update. Verify `service=cnc_machining`,
   `submission_status=persisted`, and a non-PII `transaction_id`.
3. Repeat on the Thai route with `?culture=th&item=cnc-machining`. Confirm the event name,
   parameter names, service value, trigger, and tags are identical to English.
4. Submit one quotation with a clean attachment. Confirm `file_upload_start` is secondary and
   `file_upload_complete` appears only after storage/link completion. Repeat with a rejected or
   failed attachment and confirm no completion event appears.
5. Exercise LINE, Messenger and WhatsApp (when links are present), telephone, email, Instagram,
   and YouTube links. Confirm each uses its separate secondary event and never fires the Ads
   primary tag. Confirm a Messenger outbound click is not classified as a Facebook referral, and
   reserved Maps, TikTok, and Threads events do not fire until their application instrumentation
   is approved.
6. Reject optional cookies and repeat all click checks. Confirm no analytics or Ads event fires.
7. Inspect every event payload and browser request for PII, access tokens, refresh tokens, session
   identifiers, filenames, and free-form message content. The check fails if any are present.

## GA4 DebugView

1. Enable debug mode only in the GTM Preview session.
2. In GA4 DebugView, verify one `request_quote` for each persisted test request and no event for a
   validation failure, reCAPTCHA rejection, service failure, or simple page view.
3. Compare Thai and English submissions and confirm identical event/parameter names.
4. Mark `request_quote` as a GA4 key event. Keep all click and upload events non-key unless a future
   approved measurement change explicitly says otherwise.
5. Confirm no user-provided text or direct identifier appears in DebugView.

## Google Ads conversion diagnostics

1. Import or configure only the persisted `request_quote` action as the account-default primary
   conversion used by Search and Performance Max bidding.
2. Set all contact-click and upload actions to secondary and exclude them from account-default
   goals. Keep retired Google-hosted lead form actions removed from campaign goals.
3. Use Google Ads conversion diagnostics and Tag Assistant to verify the conversion ID/label,
   consent state, transaction ID, and one-fire behavior.
4. After publication, allow for Google Ads processing delay, then verify the action reports
   `Active` without an unverified-tag, duplicate, or consent warning. Do not generate fake
   production requests merely to increase counts.

## Malware and publication gate

Before publishing the GTM container version:

- review the version diff and remove unknown tags, triggers, variables, Custom HTML, and scripts;
- verify every external hostname is expected and uses HTTPS;
- confirm Tag Manager shows no malware-flagged tag or container warning;
- check Search Console Security Issues and Google's Safe Browsing status for the production host;
- record the GTM version, approver (the sole developer is valid), Preview evidence, GA4 DebugView
  evidence, Ads diagnostics, and rollback version in the migration project.

If GTM, Search Console, or Safe Browsing reports malware, do not republish the affected tag. Disable
all its triggers and sequencing references, publish the cleaned version, and wait for Google's
automatic rescan. Application deployment and GTM container publication are separate rollback
boundaries.
