# Thai lookup consumer preparation

This is unvalidated consumer preparation for AppHost #144, using concrete Catalog candidate definitions at 6b591f86a720ae4953d9bd33c93724f4b5703b89 and #44 comment6010909664, #45 comment6010910043, #46 comment6010910391. Conflicts use string field identifiers, with null uniqueness fields omitted by Catalog. Producer hosted acceptance, Web hosted validation and shared-page handoff are pending. No deployment or provider enablement is included.

New reusable pieces: Application wire projections/validation, Infrastructure workload client/registration, Web boundary/registration, static localized lookup component and existing-field surface mappings, progressive JavaScript, Thai resources, client/HTTP/browser regression sources. These are new files only. Existing Web #493 account/inquiry resources, forms, startup and workflow files remain owned by migration. Shadcn is untouched.

## Required owner hooks

After existing infrastructure registration, call `builder.Services.AddThaiLookupClient()` and `builder.Services.AddThaiLookupBoundary()`. After the existing `UseRateLimiter`, call `app.MapThaiLookupEndpoints()`. Existing Catalog endpoint configuration and workload token provider remain the transport owner. Confirm Catalog permissions admit that workload identity. Do not propagate staff permissions or service tokens to customer/browser principals.

Insert `ThaiLookupSurface` inside each existing POST form, using its fully qualified component name from `Legacy.Maliev.Web.Components.Lookups`. Supply unique `Id`, a supported `Surface`, and the actual form antiforgery field name if customized. The static component loads its own progressive script; repeated script tags initialize each widget once. Existing manual inputs and submit handlers remain the persistence owner. These inserts are NOT yet applied or accepted.

| Surface | Planned hook | Mapped fields | Persistence owner / limit |
| --- | --- | --- | --- |
| Member profile | MemberProfileContent company fieldset | CompanyName, TaxNumber | Existing CustomerProfileUpdate; registrar is not inferred from company type |
| Member billing | MemberAddressContent billing fieldset | State, City, PostalCode, Address1 | Existing CustomerAddressUpdate; supply actual Thailand CountryId string to ThaiCountryValue |
| Member shipping | MemberAddressContent shipping fieldset | State, City, PostalCode, Address1 | Same; preserve Building and free-text Address2 |
| Instant quotation company | InstantQuotationCustomerForm | Company, TaxNumber | Existing profile-completion relationship; readonly account values remain locked |
| Instant quotation billing | InstantQuotationCustomerForm billing fieldset | BillingProvince, BillingCity, BillingStreet2, BillingPostalCode, BillingStreet1 | Existing profile-completion path; Country uses existing name-valued Thailand option |
| Instant quotation shipping | InstantQuotationCustomerForm shipping fieldset | ShippingProvince, ShippingCity, ShippingStreet2, ShippingPostalCode, ShippingStreet1 | Existing shipping-country value and ship-to-billing behavior retained |
| Public Contact company | ContactFormFields editable branch only | Company | Existing ContactSubmission, company name only |
| Public manual Quotation company | QuotationFormFields editable branch only | Company, TaxNumber | Existing typed quotation submission, without company-detail enrichment |

Surface identifiers respectively: member-company, member-billing, member-shipping, instant-company, instant-billing, instant-shipping, inquiry-company, quotation-company. Member generic address storage has no dedicated subdistrict property: chosen tuple is displayed, but Address2 is never silently repurposed. A separate schema/producer decision is required for durable administrative codes or a dedicated member subdistrict. Lookup codes are transient filters, not hidden authoritative saved IDs.

Contact/Quotation trusted-customer display branches contain no editable fields; they must not get lookup hooks. Contact has no address editor. Order address summaries are read-only. Signup has no company/address entry to enhance. CNC runtime/entry and any additional order data-entry surface require final inventory/handoff before claiming coverage. Intranet and Catalog adapters are separately owned. No existing route is claimed covered merely because its field mapping is prepared.

## Behavior and contract limits

The address widget uses explicit tuple selection, preserving postcode ambiguity. Postcode-first sends an empty name query plus a postcode constraint. Stable selected codes constrain subsequent searches; editing a parent clears incompatible editable children and transient codes. House/building/road text is untouched unless the user explicitly reviews and opts into applying detail text. Non-Thai manual values remain available. Paste conflicts offer correction or explicit retry without current filters; locked account mismatches reject the whole application. Complete-set uniqueness remains the producer's responsibility; this consumer never infers uniqueness from a preview.

Company search currently uses name suggestions. Tax-ID search and detail enrichment are deliberately not enabled from an unconfirmed capability. Null provider facts remain unknown; no status/type/objectives/address is synthesized or persisted. Only explicitly reviewed editable company name/tax fields can be applied. Creden access rights and production setup remain unresolved with the Catalog owner.

Same-origin routes are bounded POST lookups, require the existing antiforgery token, rate limit at 30 per minute, return no-store, and do not modify customer data. Raw pasted addresses and company queries are not logged by the new code. Catalog replies are bounded to 256 KiB and a 10-second operation deadline; authentication failures are mapped to unavailable and invalidate the workload token. 400/422/503 stay distinct. Existing hosted tracing/logging must still be reviewed for request-body capture before release.

## Validation status

Authored focused regression sources: 14 client wire/bounds/failure/provenance rows, seven isolated actual HTTP antiforgery/rate/failure rows, and eight controlled rendered-component browser rows (English/Thai postcode keyboard selection, locked fields, company unavailable/manual correction, ambiguous paste, non-Thai behavior, late interactive insertion and stale-response rejection). These 29 rows are a source forecast, not passed evidence. Browser sources reuse the existing admitted fixture. These fixtures are not joined Catalog or CustomerService persistence proof.

Local heavy SDK/browser/container/service execution is prohibited for this lane. Required hosted sequence: zero-warning release builds first; focused ThaiLookup tests; full affected Web suite; formatter, dependency/secret/assets/architecture gates; then actual Catalog contract/HTTP and customer save/readback for each accepted hook. Existing Web pricing/coverage/release gates must not be weakened. The resumed owner instruction explicitly authorizes candidate commits/draft PR and hosted validation to make the work reviewable. Candidate publication does not imply validated acceptance; no merge or deployment is authorized.
