#
Current integration disposition: PR761 candidate `63a1d4b8972ad24262dbcedd8ef9739c4aa6da5a` supersedes this historical ownership proposal with explicit `TestingWebRootLease` creation ownership. The earlier provider-discovery implementation is not restored. Its ten cases map to retained `WebSurfaceOwnedLifetimeTests`: two direct/configured factories, four quotation owners, four failed-start combinations (including the two shared-factory cases), lease-resolution failure, and actual Kestrel dynamic-port/static-response behavior. Those checks also preserve borrowed-provider isolation. PR738 retains only its three-line original child-start failure diagnostic beyond the PR761 ownership implementation. Exact-head and protected-main validation remain required.
Web738 residual ownership repair and custody diagnostics

Source proposal at exact head `923f2d14617640d244f7c35cd667afd06a7ba58f`, tree `9c93f1ed7a9a2a0cc5ca8039e2e575eb77a321d9`. Author execution stays source-only. Root owns refs, publication, official evidence collection and GitHub Actions.

## Two distinct failures

Root's immutable complete response blob `512cb46ca5940bab56a4768333f027fc44aa1085` records required run 39696097461 / job 119553719593: 3305 passed, 1006 failed, 0 skipped, 4311 total. At 2026-10-22T00:24:15.6107486Z the first reported failure is the CNC stop-refusal control's null recovery assertion. At 00:33:30.9785463Z quotation-submission tests begin failing; the first explicit inotify 128 exception is at 00:33:32.1772519Z in Program.cs21/default JSON watcher. Earlier antiforgery GETs return 500. The console does not contain those 500s' server exceptions or the CNC startup exception/receipt.

The CNC control assigns recovery only after scope.StartChild returns. A null recovery means startup threw before assignment. Its later Assert.NotNull masks that original exception. The diagnostic addition rethrows that exact retained exception before the masking assertion; all original cases and assertions remain. It is not a claimed fix for the unknown startup failure. Root-sealed failed-case TRX/receipt evidence is needed to establish its specific cause. No startup retry or custody/identity waiver is introduced.

## Residual watcher ownership

The previous ownership repair covered only OwnedWebSurfaceParent. InstantQuotationSubmissionEndpointTests.CreateFactory independently creates TestingWebApplicationFactory instances; those still reached the framework's unowned physical web-root path. Four other quotation test classes cloned raw class-fixture factories per case, leaving their hosts alive until class-end cleanup.

TestingWebApplicationFactory now delegates stock CreateHost through a forwarding IHostBuilder adapter. Build captures the existing physical-web-root-owning host adapter. Stock TestServer/Kestrel startup and private dynamic-port configuration remain authoritative. Failed startup disposes the captured host; wrapper-construction failure also disposes the just-built host and any captured leaves. Original failures remain primary.

The four quotation classes retain their original per-case configuration and test bodies, use the owning Testing fixture, and dispose their configured child per case. The interactive constructor owns the child before eager client creation, with reverse cleanup on failure. No production, provider, schema, dependency or workflow source changes.

## Regression coverage and preservation

Ten new rows cover direct/configured factories, sibling isolation, all four per-case quotation owners, failed startup with and without disposal failure, wrapper-construction failure, and actual Kestrel dynamic-port HTTP serving of the existing route-about.css asset.

All 4311 existing cases remain, including the 42 Career client cases and 3 prior web-root ownership cases. Expected total is 4321, subject to hosted discovery. Existing quotation test bodies are byte-identical. Literal inverses restore every modified existing source. No filter, skip, threshold, polling, sysctl, global configuration, concurrency or required-check change.

Independent read-only review inspected the eight watcher postblobs and found no blocking source defect. This is source review, not executed validation. The CNC diagnostic is separately reviewed.

## Required hosted validation

All author-side .NET builds/tests/formatting are **not run** in the source-only lane. Root must run Release build with zero warnings/errors, ownership/WebSurface/CNC/quotation/Career focused checks, the unchanged full suite and every required static, browser, contract and coverage gate on the exact new head. The initial CNC cause remains unresolved until the retained original exception is inspected and corrected if necessary.

Career producer publication stays held until Web738 reaches accepted protected main. Comment/TrackingNumber, FX, financial/provider execution, deployment and production data remain held. Author native resources created: 0; the source-review subagent also created none.
