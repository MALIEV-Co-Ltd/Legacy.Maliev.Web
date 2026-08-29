# Daily source parity checkpoint

This file records the resumable checkpoint for the automated migration of committed
changes from the read-only `R:\maliev-web` source into `Legacy.Maliev.*` repositories.
It does not authorize source edits, pushes, deployments, database writes, GKE changes,
or Google configuration publication.

## Current boundary

- Proven historical Legacy Web checkpoint: `48e628cf7803264bd0b09bfa7a55b15b47e192dd`.
- Source branch inspected: `main`.
- Source head inspected on 2026-08-29: `54ad353a386dc9476f779f9b3fdf152b93b135c9`.
- Committed source changes after the historical checkpoint: 123.
- Daily automation: `daily-legacy-maliev-migration-parity`, scheduled for 08:00 Asia/Bangkok.
- Uncommitted source files are deliberately excluded from parity accounting.

## Migrated commits after the historical checkpoint

| Source commit | Source outcome | Legacy repository | Legacy commit | Validation |
| --- | --- | --- | --- | --- |
| `25db5545b0f5f575f61616af2ba54ee45e656a20` | Make custom manufacturing the canonical owner for process-unknown search intent across the custom, CNC, 3D-printing, and 3D-scanning routes | `Legacy.Maliev.Web` | `b363dd6` | Release build: 0 warnings/errors; focused SEO and structured-data suite: 28/28; affected service-route suite: 75/75; format clean |
| `bf4c550cb28fd0432d9c178a7072d07d61e43e13` | Ignore HTML-looking strings in ordinary JavaScript when verifying rendered production SEO documents | `Legacy.Maliev.Web` | `41e4e0f` | Production verifier baseline restored for the extracted repository; PowerShell syntax clean; Pester production SEO contracts: 32/32 |
| `612cb5a93efb3cbf7d6c6c6122a76d9694804b51` | Slim the shared frontend payload by assigning home, About, inquiry, service, member-order, and Instant Quotation assets to their owning routes | `Legacy.Maliev.Web` | `2010c89` | Release build: 0 warnings/errors; browser modules: 101/101; public-page and asset slice: 99/99; Instant Quotation/member slice: 502/502; rendered route-asset contracts: 35/35; format clean |
| `a521f6969b7460240ed21925aad157142b489e8e` | Deliver responsive mobile variants for service heroes and high-cost 3D-printing application imagery | `Legacy.Maliev.Web` | `73548c8` | 22/22 committed image blobs match source; Release build: 0 warnings/errors; affected service suite: 113/113; dedicated source and rendered-route contracts: 17/17; format clean |
| `656ee29576839f16c9af8422283f321cdfe612cb` | Reject invalid language-switch cultures without losing the canonical English/Thai URL contract | `Legacy.Maliev.Web` | `8ccb942` | Release build: 0 warnings/errors; focused localization, navigation, and canonical suite: 29/29; format clean |
| `d173d0bbc9c77958e2fd49f50cdb1d25531e33f6` | Replace the province-limited 3D-printing title with the approved nationwide Thai and English commercial title across Blazor and retained fallback rendering | `Legacy.Maliev.Web` | `6a4c8d1` | Release build: 0 warnings/errors; focused SEO contracts: 24/24; 3D-printing suite: 25/25; production SEO Pester contracts: 32/32; format clean |
| `e36435a51aa5471a19a790372fa6e43bc5f403c6` | Contain quotation crawl routes by excluding the utility from the sitemap, applying HTML and HTTP noindex, permanently consolidating supported legacy service paths into query-prefilled URLs, and rejecting arbitrary or surplus segments | `Legacy.Maliev.Web` | `0950ac1` | Release build: 0 warnings/errors; focused quotation, sitemap, and SEO suite: 30/30; production SEO Pester contracts: 32/32; format clean |
| `30a22afb243306726b820cbd0eaee0977600e331` | Define Custom Manufacturing as the initial owner for process-unknown or multi-process requests, capture procurement inputs, and state the quotation output contract | `Legacy.Maliev.Web` | `6d566d2` | Release build: 0 warnings/errors; custom-manufacturing and SEO suite: 21/21; bilingual ownership/input/output rendering and specialist links covered; format clean |
| `55fa9615f3ffc85c771e31a773184b80bde6b894` | Integrate the complete responsive service-image delivery contract | `Legacy.Maliev.Web` | `73548c8` | Accounted by the earlier target-native responsive-image slice: 22/22 source image blobs match; affected service suite: 113/113; dedicated responsive contracts: 17/17 |
| `d6908049549f75b5e13c1858f4e5e2c2e0276752` | Make the quotation route middleware activatable and align the production SEO route inventory after quotation became a noindex utility | `Legacy.Maliev.Web` | `0950ac1` | Public constructor/invoker reflection contract included; Release build: 0 warnings/errors; focused quotation, sitemap, and SEO suite: 30/30; production SEO Pester contracts: 32/32 |
| `791126cb1ae2a92774ec72528b84bff4d0d6a499` | Emit browser-resolvable root-relative responsive service image candidate URLs | `Legacy.Maliev.Web` | `73548c8` | Accounted by the target-native responsive-image slice; rendered-route tests resolve the responsive candidates and no tilde-prefixed candidate URLs remain |

## Supporting migration maintenance

| Legacy commit | Outcome | Validation |
| --- | --- | --- |
| `65be64c` | Pin patched `SSH.NET` 2026.0.0 over the vulnerable Testcontainers transitive version | Restore succeeded; NuGet vulnerable-package audit reports none; Release build: 0 warnings/errors |

## Classified source commits without a runtime port

| Source commit | Classification | Evidence |
| --- | --- | --- |
| `ffffbdd60daf573dc76038a5200784b530d36c55` | Historical .NET 8 implementation/deployment plan; no runtime behavior to migrate. Its approved nationwide-title outcome is tracked separately by the later implementation commit `d173d0b`. | Source commit changes only two planning documents and explicitly contains source-repository deployment and Search Console execution steps that must not be copied into the .NET 10 runtime repository. |
| `95edbfbadda5a5e201733c9a83fb6b1eeea73df2` | Source-only .NET 8 Playwright fixture stabilization; no compatible file exists in the migrated test architecture. | The source change manages the .NET 8 executable, jQuery/development vendor copies, and browser contexts. Legacy Web removed jQuery and uses .NET 10 static-SSR integration plus deterministic Node browser-module contracts; copying the fixture would restore eliminated dependencies rather than preserve runtime behavior. |
| `80dd3df1d4de30586c6ecd84b848f4236eade3db` | Historical design document for the service-ownership SEO release; runtime outcomes are tracked commit-by-commit in the following implementation slices. | Source commit changes only a design document and includes source-release operations that are not runtime artifacts. |
| `dc534658f3d7d3271b2788e99517372ca787bf0e` | Historical .NET 8 implementation/deployment plan for the same SEO release; runtime outcomes are tracked by `e36435a`, `30a22af`, `7b1a8fd`, `c317cac`, and related implementation commits. | Source commit changes only a plan containing Razor Pages, source deployment, GKE rollout, and Search Console execution instructions; copying it would misstate the .NET 10 migration workflow. |
| `258ed4aef122737511e27c8aac35130b59d4b30e` | Generated .NET 8 XML documentation artifact; no runtime or compatible tracked artifact to port. | The target does not track a root generated documentation XML file. The meaningful middleware XML comments are present in the .NET 10 source and the Release build remains warning-free. |

## Remaining gate

The other 107 committed source changes after `48e628c` remain pending commit-by-commit
classification and migration. They include public Web SEO, structured data, responsive
assets and layouts, consent-gated analytics and Ads measurement, quotation lifecycle,
authentication/profile behavior, service tracing and logging, pricing, operational
configuration, and changes owned by non-Web `Legacy.Maliev.*` services.

The complete Legacy Web test suite currently has an independent resource defect: the
test host exceeded 13 GB working set without producing a result. The affected bounded
suites recorded above pass. The complete-suite resource issue must be isolated before this
checkpoint can be promoted from focused validation to full-suite validation.
