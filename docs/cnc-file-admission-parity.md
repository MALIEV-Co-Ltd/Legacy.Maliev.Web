# CNC file-admission migration evidence

Source checkpoint: `5ac7d045c51194edd9e64d8564f1b726b001be34`.
Tracking: [Web #195](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/195), under [commit register #188](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/188).

## Ported boundary

`CncFileAdmissionValidator` preserves the source STEP envelope/deep inspection, IGES fixed-record/entity ownership, and PDF object/xref/page-tree algorithms. Namespace and nullable annotations are .NET 10 adaptations. Source upload admission uses `HasValidStepEnvelope`, not the stricter `IsValidStep`; the eventual handler must preserve that distinction.

`CncCommercialCalibration` preserves the server-only ledger and published recovery coefficient. The rollout gate retains source `cnc-commercial-v5`; browser configuration retains source `cnc-commercial-v6`. This port deliberately does not normalize these distinct source contracts or expose the internal ledger to the browser.

Source commits touching these files: `e5f5b56`, `29a8c2e`, `e0c07ff`, `ba3f34d`, `120db56`, `4c32868`, `b5da940`, `1d15ef6`, `0cfe7e6`, `49b6d72`, `79060bd`. Porting these files does **not** complete all behavior in those commits.

## Regression coverage

- Calibration recovery, margin floor, public coefficient, and private-ledger exclusion.
- Real STEP envelope, malformed surface curves, fabricated topology, and compact STEP records.
- Marker-only inputs, unknown/empty IGES entities, malformed numeric fields.
- Invalid classic PDF xrefs, recursive page trees, xref/object streams, and oversized numeric widths.
- Existing rollout availability tests remain unchanged.

## Remaining integration

- Dedicated CNC GET page and final submission handler, including profile and browser integration.
- Production shared atomic receipt store and historic Data Protection key-ring compatibility remain unproven. Matching protection purposes does not prove compatibility between different application discriminators or key rings.
- Browser and typed service integration evidence for the actual CNC workflow.

## Protected upload integration

PR #197 supplies the typed generic `/Uploads` transport and confirmed compensating deletion. The dedicated POST handler now preserves the protected `iq_session` cookie, form/item/role/filename/path bindings, three-hour lifetime, receipt reservation before object creation, and locked state when cleanup is ambiguous. It retains source 25 MiB model / 10 MiB drawing limits and STEP envelope admission. The route requires antiforgery and remains unavailable without the existing rollout and receipt-store gates; production cannot use the development in-memory store.

The unowned IGES parameter regression uses the existing `cube.iges` fixture, whose Git blob exactly matches source `cube.igs`. HTTP-pipeline tests cover ambiguous admission rejection and route antiforgery metadata; handler tests cover protected bindings and transport outcomes. These are not live GCS, historic-token, browser, or full quotation-submission evidence.

No deployment or database change is performed. This slice does not complete source-commit or end-to-end CNC parity.

Validation for this integration: Release build 0 warnings/0 errors; 94 focused tests and 1,714 full Web tests passed with no skips. A failing-then-passing cancellation regression verifies that cancellation before transport entry returns `NotSent`, releases the reservation, and never attempts compensating deletion. Changed-file secret scanning passed. Live storage, browser, and container checks remain pending because this is not yet the complete CNC workflow or a deployed candidate.
