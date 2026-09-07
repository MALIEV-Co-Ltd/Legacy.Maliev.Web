# CNC authenticated account preparation

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`, read through committed Git objects only. This is preparation for the pending CNC submission coordinator, not a complete feature or an enabled endpoint.

## Exact source mapping

- `Maliev.Web/Pages/InstantQuotation/InstantQuotationAuthenticatedProfile.cs`: `Create`, `Set`, `FirstValue`, `ParseTaxNumber`, `ApplyBillingAddress`, `ApplyShippingAddress`, `IsLocked`, `MergeMissing`.
- `Maliev.Web/Pages/InstantQuotation/CNC-Machining.AuthenticatedProfile.cs`: the pure mapping/relationship guards from `LoadAuthenticatedProfileAsync` and merge/revalidation from `PrepareAuthenticatedSubmissionAsync`, `CaptureCustomerDetails`, `ApplyCustomerDetails`, `RevalidateCustomerDetails`.
- `Maliev.Web/Pages/InstantQuotation/CNC-Machining.cshtml.cs`: exact required-field annotations/messages. Source's `InstantQuotationAuthenticatedProfileTests.cs` six pure cases are ported with synthetic values, alongside negative/admission tests.

`CncAuthenticatedProfile` consumes the existing typed `CustomerAccountDetails`, `CustomerCompany`, `CustomerAddress` and `Country` contracts. It introduces no customer/identity network DTO or endpoint. Its internal `CncAuthenticatedIdentity` is a trusted projection: the coordinator must derive it only from a successful, claim-bound Auth self-profile read and verify its database identifier against the current server-side login session. Never use a posted customer ID, identity email/mobile, or client-supplied account object to construct it. The helper itself additionally rejects an identity/customer ID mismatch, nonpositive reference IDs, missing nested relationships, and nested IDs that do not match the declared relationship.

## Field-by-field behavior

| Fields | Stored authority and merge behavior |
| --- | --- |
| FirstName, LastName | Customer values, trimmed; populated values locked. No identity-name fallback. |
| Email | First nonblank of customer email, identity email; trimmed and locked when populated. |
| Mobile | First nonblank of customer mobile, identity MobileNumber; trimmed and locked when populated. Telephone is never a mobile fallback. |
| Telephone | Customer telephone, independently trimmed/locked. |
| Company | Referenced company name, trimmed/locked. A tax-only company's empty name remains editable. |
| TaxNumber, TaxBranch, TaxBranchCode | Source regex parses Thai `สำนักงานใหญ่` or `สาขาที่` with one to five digits; codes are left-padded to five. Parsed populated fields lock independently. Unmatched tax text stays verbatim except trimming; no invented checksum/format rule. |
| BillingBuilding, BillingStreet1, BillingStreet2, BillingCity, BillingProvince, BillingPostalCode | Billing Building, AddressLine1, AddressLine2, City, State, PostalCode respectively; each populated component independently locked. |
| Country | Billing CountryId resolved to the first matching country Name; populated name locked. Missing country lookup remains editable, as in source. |
| ShipToBillingAddress | Any stored ShippingAddressId locks the selection. True only when a billing ID exists and matches shipping; a shipping-only account remains false. Without a stored shipping ID, defaults true but remains editable. |
| ShippingBuilding, ShippingStreet1, ShippingStreet2, ShippingCity, ShippingProvince, ShippingPostalCode, ShippingCountry | Same component mapping from the effective shipping address; source uses billing when same-as-billing is true, otherwise stored shipping. Each populated component locks independently. |
| Description, OrderItems | Preserved from submitted input, not made authoritative or admitted by profile preparation. |

Source nuance preserved: when no shipping ID exists but billing is present, effective billing values are mapped into and lock populated shipping fields, even though the same-as-billing selection remains editable. This slice does not silently redesign that behavior.

The existing `CncSubmission` uses non-null strings. Missing optional source values are represented as empty strings, but remain unlocked and invalid for required fields; null is not promoted to a valid company record. Populated stored values retain source trimming only. `Details` returns a defensive copy so UI rendering cannot mutate subsequent account authority. Merging does not mutate the posted submission or the supplied account DTOs.

## Revalidation and security boundary

After merge, old contact-field ModelState entries are removed exactly for the 24 source contact fields. The source-required fields are FirstName, LastName, Email, Mobile, Country, BillingStreet1, BillingCity, BillingProvince and BillingPostalCode. Their exact source messages and `RequiredAttribute` semantics are retained. The source has no EmailAddress/Phone/postal/tax validation annotation here, nor conditional required shipping annotations. Country resolution and any other workflow checks remain separate; passing this helper does not prove complete business admission.

Deliberate fail-closed adaptation: source `PrepareAuthenticatedSubmissionAsync` returns true after adding validation errors and relies on later ModelState checks. `TryMergeAndValidate` returns false and no merged submission whenever validation or any retained non-contact error remains. Receipt/item errors are never cleared. Positive nested ID equality checks strengthen the old null-only load guards without relaxing authorization.

Exact source coordinator order (`OnPostSubmitRequestAsync`, line 1291 onward): availability/development checks -> initial posted request budgets -> protected form validation -> item/receipt/snapshot validation and ModelState check -> atomic `TryClaimValidatedCncUploadReceipts` -> identity lookup -> authenticated profile reload/merge/revalidation -> quotation request creation. Identity lookup/preparation exceptions or a false preparation result restore the already-claimed receipts before returning retry. After request creation, fill-only profile persistence precedes analytics queueing, file linking and notifications. Once posting has begun with an uncertain outcome, or a request is known to exist, retries are terminal and receipts must not be restored.

This helper belongs inside that existing authenticated preparation position, **after** the atomic receipt claim. The future coordinator must project the claim-bound identity, load matching customer/referenced records and country inventory, then call the helper; a false result must follow the existing pre-post receipt-restoration path. It must not move claims or persistence merely because this helper is pure. Successful preparation does not authorize a network write or satisfy later gates. An additional merged-value budget recheck is recommended before posting because stored account values replace posted values; this is proposed hardening, not a claim that the source already performs that recheck, and needs explicit coordinator implementation/tests.

## Validation and remaining work

Clean worktree baseline: Release builds at zero warnings/errors; 1,835 tests passed before this slice. New focused cases: 43 passed. Full suite with coverage: 1,878 passed, zero skipped, 2 minutes 28 seconds. Web-owned coverage (Web/Application/Infrastructure, without excluding generated owned code): 17,613/20,840 lines, 84.52%. New helper: 100% lines, 96.15% branches. Initial formatter found only test initializer whitespace; scoped formatting corrected it and full format verification passed. A final Release build (zero warnings/errors) and focused 43-case rerun also passed after formatting. No vulnerable direct/transitive packages were reported for all four Web projects.

Commands use `-p:MalievWorkspaceRoot=B:/maliev-legacy -p:BuildProjectReferences=false` for builds so shared dependency outputs are not rebuilt. Local projects in a fresh worktree must be built in dependency order (Application, Infrastructure, Web, Tests). Full validation uses `dotnet test Legacy.Maliev.Web.Tests -c Release --no-build --no-restore --collect:'XPlat Code Coverage'`; focused uses `--filter FullyQualifiedName~CncAuthenticatedProfileTests`. Static commands: `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore`, `dotnet list Legacy.Maliev.Web.slnx package --vulnerable --include-transitive --no-restore`, `gitleaks git . --redact=100 --exit-code 1 --no-banner --no-color`, `gitleaks dir . --redact=100 --exit-code 1 --no-banner --no-color`, and `git diff --check`. Format/package commands use the `MalievWorkspaceRoot` environment variable.

Not implemented here: Auth self-profile HTTP client (separate slice), route/form UI wiring, database/customer reload, fill-only customer/company/address persistence, country resolution, atomic submission claims, request/file orchestration, customer/manufacturing notification, confirmation/analytics, synthetic development fixture mode, browser E2E, production-derived data validation, or deployment. No source/runtime database provider, new credentials, persistence adapter, no-op success or network endpoint is introduced.
