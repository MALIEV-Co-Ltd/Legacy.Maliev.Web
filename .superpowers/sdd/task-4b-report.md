# Task 4B Report: Authoritative Multi-Part Workflow Integration

## Outcome

- Replaced the inert upload shell with an accessible Blazor `InputFile` supporting multiple files and the exact production extension list.
- Added a server-side workflow coordinator over the existing protected session, fail-closed upload, and deterministic pricing contracts.
- Preserved selection order across concurrent uploads and rejected cancelled, stale, unavailable, non-authoritative, or mismatched results.
- Added stale-success cleanup through `RemoveAsync` with a fresh operation ID so an opaque reference is never admitted silently after cancellation.
- Added independent cancellation, retry with fresh operation IDs, recoverable remove retry without re-upload, and disposal invalidation for non-cooperative adapters.
- Persisted authoritative parts/configuration in the protected session and recomputed part/order quotes only through `IInstantQuotationPricingService`.
- Rendered catalog materials/colors, quantities, part tiers/prices, subtotal/shipping/VAT/final total, lead time, and required stable markers.
- Preserved successful multipart parts and configuration access when another upload/remove is in recoverable error.
- Derived member ownership only from an authenticated name-identifier claim; no session identity, opaque reference, storage path, token, credential, or manual geometry is rendered.

## TDD Evidence

Initial RED:

```text
dotnet test Legacy.Maliev.Web.Tests\Legacy.Maliev.Web.Tests.csproj --no-restore -p:MalievWorkspaceRoot=B:\maliev --filter FullyQualifiedName~InstantQuotationWorkflowUploadTests --verbosity minimal
CS0246: InstantQuotationWorkflowCoordinator and InstantQuotationWorkflowUploadFile did not exist.
```

Additional focused REDs reproduced before their fixes:

- Failed remove retry threw `Only failed or cancelled uploads can be retried.`
- Cancelled late authoritative success left the stale opaque reference without cleanup.
- The component instantiated the concrete pricing service instead of resolving its contract.
- Multipart error lacked a combined safe UI path to successful parts.
- Disposal against a non-cooperative upload client left the item `Uploading` instead of `Cancelled`.

## Verification

- Focused Task 4B class: 13 tests, all passing.
- Release bounded regression lane (Task 1-3 contracts, Task 4B, route/host/architecture): 204 passed, 0 failed, 0 skipped.
- `dotnet build Legacy.Maliev.Web.slnx --no-restore -c Release -p:MalievWorkspaceRoot=B:\maliev`: succeeded, 0 warnings, 0 errors.
- Scoped `dotnet format` and `--verify-no-changes`: exit 0.
- `git diff --check`: clean.
- Razor markup scan for server session/reference/storage/token/credential/manual-geometry terms: no matches.

## Stable Rendered Contract

Primary markers:

- `data-workflow-upload`
- `data-workflow-viewer`
- `data-workflow-part-list`
- `data-workflow-material-picker`
- `data-workflow-part-pricing`
- `data-workflow-order-total`
- `data-workflow-lead-time`
- `data-workflow-review`
- `data-workflow-customer-details`
- `data-workflow-submitted`

Task 3 compatibility aliases retained:

- `data-workflow-parts`
- `data-workflow-configuration`
- `data-workflow-dfm-status`
- `data-workflow-material-color-quantity`
- `data-workflow-part-price`
- `data-workflow-order-summary`

Workflow enum remains: `Empty`, `Uploading`, `Uploaded`, `Error`, `MultiPart`, `Configured`, `Review`, `CustomerDetails`, `Submitted`.

## Remaining Integration Boundaries

- Task 4A owns advisory viewer/upload JavaScript integration; this slice does not wire or modify those files.
- The component resolves `IInstantQuotationPricingService` and fails closed when the application dependency set is incomplete; integration-owned DI registration remains outside this slice.
- `IInstantQuotationUploadClient` remains fail-closed until FileService #7 publishes the exact upload/remove/finalization HTTP contract.
- Submission/finalization, customer form, auth/CSRF submission, and analytics events remain later tasks.
