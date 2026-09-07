# CNC request notification contract

This slice migrates the notification construction behavior from committed source
`R:\maliev-web` `origin/main` at `5ac7d045c51194edd9e64d8564f1b726b001be34`.

After a CNC quotation request and its files have been persisted, Web must construct
two HTML message bodies before calling the authenticated NotificationService JSON
API:

- a customer confirmation sent through the Manufacturing channel, BCC
  `mail-tracking@maliev.com`;
- a Manufacturing-channel review message to `manufacturing@maliev.com`, with the
  customer address as `Reply-To`.

Both messages use `Quotation Request #{id}` as the subject. Customer-controlled
text is HTML encoded. Every model and supplied drawing requires a complete,
absolute HTTPS signed link with no URI user information. Missing, mismatched, or
unsafe links fail closed. The customer message retains the explicit non-binding
preliminary-price warning; the internal message retains the canonical untrusted
estimate snapshot and engineering-review requirement. Bodies exceeding the
NotificationService 100,000-character API limit fail before dispatch.

This commit deliberately does not invoke NotificationService and does not map the
CNC submission route. Signed-link acquisition, ordered idempotent dispatch, and
terminal outcome integration remain separate issue #195 slices.
