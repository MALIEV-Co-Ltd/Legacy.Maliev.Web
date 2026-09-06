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

- Dedicated CNC page and upload/submission handlers, including protected session/form/item/role binding and atomic receipt lifecycle.
- PDF drawing transport: Web's capability store and FileService transport and FileService's producer currently publish an exact CAD-only extension contract. Do not simply reuse the additive geometry workflow for PDF drawings.
- Preserve source 25 MiB model / 10 MiB drawing limits in the CNC route while retaining existing additive contracts.
- Extract the source unowned-IGES-parameter fixture case (`TestAssets/cube.igs`).
- Browser and typed service integration evidence for the actual CNC workflow.

No route is enabled, deployment is performed, or database state changed by this helper port. Standalone validator tests are not proof of end-to-end upload or migration completion.
