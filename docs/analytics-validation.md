# Legacy Web analytics and conversion validation

This runbook defines the production contract for the public legacy website. It applies to the
`Legacy.Maliev.Web` image deployed in the existing `maliev-legacy` namespace and to Google Tag
Manager container `GTM-KHDDLVRR`. It does not authorize a deployment or a Google container publish
by itself.

## Conversion contract

| Event | Purpose | Ads classification |
| --- | --- | --- |
| `request_quote` | A contact or quotation request that the backend persisted successfully | Primary Ads conversion |
| `file_upload_start` | A selected quotation attachment begins the verified form submission path | Secondary only |
| `file_upload_complete` | Every selected attachment was malware-scanned, stored, and linked | Secondary only |
| `line_click` | LINE Official Account click | Secondary only |
| `messenger_click` | Messenger click | Secondary only |
| `whatsapp_click` | WhatsApp Business click | Secondary only |
| `phone_click` | Business telephone click | Secondary only |
| `email_click` | Business email click | Secondary only |

`request_quote` is the only event in this table that may be a primary Google Ads bidding
conversion. Page views, service/product views, add-to-cart events, social clicks, file events,
Google-hosted lead form submissions, LINE, Messenger, WhatsApp, telephone, and email clicks must
not be primary conversions.

The `request_quote` data-layer payload is non-PII and contains only:

- `intent_type`: `contact_request` or `quotation_request`;
- `service`: `general_contact`, `3d_printing`, `3d_scanning`, `cnc_machining`,
  `injection_molding`, or `custom_manufacturing`;
- `transaction_id`: an opaque persisted reference such as `message-913` or `quotation-713`;
- `submission_status`: always `persisted`;
- `has_files` and `file_upload_completed`: booleans.

Do not add names, email addresses, telephone numbers, company names, message text, filenames,
full URLs, user IDs, or authentication/session values to any analytics payload.

## GTM configuration

1. Create Data Layer Variables using data-layer version 2 for `intent_type`, `service`,
   `transaction_id`, `submission_status`, `has_files`, `file_upload_completed`, `channel`,
   `destination`, `context`, and `file_count`.
2. Create one Custom Event trigger named `CE - request_quote - persisted` with event name
   `request_quote` and the condition `submission_status equals persisted`.
3. Create one Custom Event trigger for each secondary event listed above. Do not combine a click
   trigger with the primary trigger.
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
5. Exercise LINE, Messenger, WhatsApp (when a link is present), telephone, and email links. Confirm
   each uses its separate secondary event and never fires the Ads primary tag.
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
