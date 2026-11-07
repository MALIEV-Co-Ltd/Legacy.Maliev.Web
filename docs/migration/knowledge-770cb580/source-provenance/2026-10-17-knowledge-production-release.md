# Knowledge Center production release preparation

Approved by owner: deploy the pending Knowledge Center changes to the current Maliev.Web production application using this checkout's deploy_web.ps1.

## Scope

The release includes the researched19-page manufacturing library, grouped sidebar navigation and local English/Thai search, full-item hover feedback, linear article contents, reviewed/revised engineering SVGs, CNC/inspection replacement photography, warpage/chatter illustrations and literal search-count formatting regression coverage. No API, database, pricing, authentication, analytics or Shopify changes belong to this release. Unrelated untracked home/about/customer-logo assets stay outside the release checkout.

The current production namespace is maliev in web-production-cluster (us-central1-a, project maliev-website), deployment maliev-web. The pre-release image is v2026.10.16.1752-e1edc6d. Remote main e1edc6de1537712f52c016318e311c8dd3819e5c is an ancestor of this branch; it was7 commits behind the prepared source before final correction commits. These observations must be rechecked at publication. The exact final commit and deployed image will be recorded in the external release receipt.

## Validation

- Fresh Release build: zero warnings and errors, twice (45.60s initial,21.84s after test correction).
- Initial focused run:63 passed,2 failed; failures came from an invalid assertion that responsive HTMLImageElement.naturalWidth equals the source-file width. naturalWidth is density-corrected CSS pixels. The source files still decode at1536x1024/768x512. The test now checks successful decode,3:2aspect ratio and the intended responsive asset source. Reference: https://developer.mozilla.org/en-US/docs/Web/API/HTMLImageElement/naturalWidth.
- Corrected focused run:65 passed,0failed,0skipped. Both specifications formatting cases return200 and preserve localized count placeholders.
- Scoped C#whitespace formatting for the two touched browser-test files: passed.
- Security scanner tests:26 passed. Current-tree scan:0 high-confidence findings.
- Static asset/resource checks:19SVGs parse,8affected English/Thai resource sets have unique matching keys, responsive photos decode at expected source sizes, and git diff --check passes.
- Root reviewed fresh native-browser desktop/mobile EN/TH contents, warpage/chatter and inspection captures, plus the revised CNC preparation and hole-depth diagrams. No UI correction was needed in this inspection batch.
- Full Web suite: 1,208 passed, zero failed/skipped (11 minutes 26 seconds). JavaScript: 48 passed; frontend: 78 passed.
- Final live loopback specifications overview: HTTP 200 in English/Thai, literal count placeholders present. The temporary preview and review tab were stopped/closed.
- Anodizing browser review identified missing dimension endpoint arrowheads caused by multiple SVG subpaths sharing start/end markers. Split each dimension into its own path, XML-parsed and rechecked in the browser; all heads now render. Final all-guide asset-decode rerun: 4 passed, zero failed/skipped, covering this static-only change before packaging.

Prior resource-blocked attempts are recorded in their own verification notes; they are not counted as passed checks. Native work is serialized under the unchanged4 GiB admission guard,2 logical processors,2 GiB managed heap and finite stage leases. Full worker identities and final cleanup receipts live under D:/codex-temp/2026-10-14/task/knowledge-production-release.

## Release sequence

Complete full checks and final browser inspection, commit logical source slices, publish the exact revision and monitor its GitHub Actions workflow to completion. Deploy from a clean source checkout using deploy_web.ps1, which builds/pushes locally through Docker rather than Cloud Build. Verify rollout, ready workload image digest and public English/Thai Knowledge Center pages. Preserve unrelated work and clean task-owned workers and the temporary release checkout.
