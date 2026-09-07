# CNC authenticated profile fill-only persistence

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`.
The original repository was inspected through committed Git objects only.

This slice adds the dedicated customer-service client needed after a CNC
quotation request has already been created. It deliberately does not reuse the
member profile editors: those are replacement workflows with unlink and
compensation behavior, while the source CNC flow only fills missing values.

The client preserves the source write order: company, billing-country lookup,
billing address, optional distinct shipping-country lookup and address, then
customer scalar/reference update last. Existing `Fax`, `DateOfBirth` and
company `Registrar` values are preserved. Same-as-billing reuses the confirmed
billing address identifier. A distinct blank or unknown shipping country never
falls back to the billing country.

All results retain the already-created quotation request identifier. A rejected,
uncertain, malformed or interrupted write is terminal for that quotation; the
caller must not recreate the quotation, replay this non-idempotent workflow,
restore claimed upload receipts or delete/restore partially written profile
records. Confirmed created identifiers are retained for reconciliation without
including customer-entered values.

Created responses require HTTP 201, a bounded PascalCase JSON object, a positive
matching `Id`, matching returned fields and an exact same-origin `Location`.
Updates require HTTP 204. The named clients retain unsafe-method retry disabling,
and bearer service tokens remain server-side.

This preserves the source limitation: CustomerService has no ETag, xmin/CAS or
atomic fill-only endpoint, so a full-record PUT derived from a snapshot cannot
claim concurrency safety. Removing that race requires a separately reviewed
producer contract and rollout order.

Focused tests cover source ordering, create/update/no-op behavior, same and
distinct shipping, preservation of authoritative values, PascalCase payloads,
strict response validation, definite rejection, uncertain transport outcome,
token invalidation, preflight failure, no delete compensation and DI
registration. Coordinator wiring, notification delivery, file finalization,
route/UI behavior, browser E2E and production-derived data validation remain
separate gates under Web issue 195.
