# Daily source parity checkpoint

This file records the resumable checkpoint for the automated migration of committed
changes from the read-only `R:\maliev-web` source into `Legacy.Maliev.*` repositories.
It does not authorize source edits, pushes, deployments, database writes, GKE changes,
or Google configuration publication.

## Current boundary

- Proven historical Legacy Web checkpoint: `48e628cf7803264bd0b09bfa7a55b15b47e192dd`.
- Source branch inspected: `main`.
- Source head inspected on 2026-09-05: `4486f0e964e508e5eb7b43a59eeaec46cc052c67`.
- Committed source changes after the historical checkpoint: 159.
- Daily automation: `daily-legacy-maliev-migration-parity`, scheduled for 08:00 Asia/Bangkok.
- Uncommitted source files are deliberately excluded from parity accounting.

## Migrated commits after the historical checkpoint

| Source commit | Source outcome | Legacy repository | Legacy commit | Validation |
| --- | --- | --- | --- | --- |
| `3f090e9488d91790636558d3ce59c7f046efa1d8` | Keep completed additive pricing tiers and the next quotation action reachable at compact viewport sizes | `Legacy.Maliev.Web` | `3a489e7` | Target-native Blazor pricing region is keyboard-focusable and independently scrollable; responsive geometry and Tab-order regression at 375×667, 820×800, and 1280×800: 4/4. Release build: 0 warnings/errors; full Web suite: 2162/2162; format clean. Browser fixture uses representative completed pricing DOM because no uploaded part is available in local preview; this is not a production-derived quotation acceptance test. |
| `25db5545b0f5f575f61616af2ba54ee45e656a20` | Make custom manufacturing the canonical owner for process-unknown search intent across the custom, CNC, 3D-printing, and 3D-scanning routes | `Legacy.Maliev.Web` | `b363dd6` | Release build: 0 warnings/errors; focused SEO and structured-data suite: 28/28; affected service-route suite: 75/75; format clean |
| `bf4c550cb28fd0432d9c178a7072d07d61e43e13` | Ignore HTML-looking strings in ordinary JavaScript when verifying rendered production SEO documents | `Legacy.Maliev.Web` | `41e4e0f` | Production verifier baseline restored for the extracted repository; PowerShell syntax clean; Pester production SEO contracts: 32/32 |
| `612cb5a93efb3cbf7d6c6c6122a76d9694804b51` | Slim the shared frontend payload by assigning home, About, inquiry, service, member-order, and Instant Quotation assets to their owning routes | `Legacy.Maliev.Web` | `2010c89` | Release build: 0 warnings/errors; browser modules: 101/101; public-page and asset slice: 99/99; Instant Quotation/member slice: 502/502; rendered route-asset contracts: 35/35; format clean |
| `a521f6969b7460240ed21925aad157142b489e8e` | Deliver responsive mobile variants for service heroes and high-cost 3D-printing application imagery | `Legacy.Maliev.Web` | `73548c8` | 22/22 committed image blobs match source; Release build: 0 warnings/errors; affected service suite: 113/113; dedicated source and rendered-route contracts: 17/17; format clean |
| `656ee29576839f16c9af8422283f321cdfe612cb` | Reject invalid language-switch cultures without losing the canonical English/Thai URL contract | `Legacy.Maliev.Web` | `8ccb942` | Release build: 0 warnings/errors; focused localization, navigation, and canonical suite: 29/29; format clean |
| `d173d0bbc9c77958e2fd49f50cdb1d25531e33f6` | Replace the province-limited 3D-printing title with the approved nationwide Thai and English commercial title across Blazor and retained fallback rendering | `Legacy.Maliev.Web` | `6a4c8d1` | Release build: 0 warnings/errors; focused SEO contracts: 24/24; 3D-printing suite: 25/25; production SEO Pester contracts: 32/32; format clean |
| `e36435a51aa5471a19a790372fa6e43bc5f403c6` | Contain quotation crawl routes by excluding the utility from the sitemap, applying HTML and HTTP noindex, permanently consolidating supported legacy service paths into query-prefilled URLs, and rejecting arbitrary or surplus segments | `Legacy.Maliev.Web` | `0950ac1` | Release build: 0 warnings/errors; focused quotation, sitemap, and SEO suite: 30/30; production SEO Pester contracts: 32/32; format clean |
| `30a22afb243306726b820cbd0eaee0977600e331` | Define Custom Manufacturing as the initial owner for process-unknown or multi-process requests, capture procurement inputs, and state the quotation output contract | `Legacy.Maliev.Web` | `6d566d2` | Release build: 0 warnings/errors; custom-manufacturing and SEO suite: 21/21; bilingual ownership/input/output rendering and specialist links covered; format clean |
| `7b1a8fd28c54f465c0ac966b3cf132ec49680bf7` | Complete service structured data with truthful bilingual finishing-and-colour schema and require recursively discoverable Service JSON-LD on every service detail route | `Legacy.Maliev.Web` | `02721f7` | Release build: 0 warnings/errors; structured-data, finishing, and SEO suite: 19/19; production SEO verifier: 34/34 including nested-Service pass and missing-Service failure fixtures; format clean |
| `55fa9615f3ffc85c771e31a773184b80bde6b894` | Integrate the complete responsive service-image delivery contract | `Legacy.Maliev.Web` | `73548c8` | Accounted by the earlier target-native responsive-image slice: 22/22 source image blobs match; affected service suite: 113/113; dedicated responsive contracts: 17/17 |
| `d6908049549f75b5e13c1858f4e5e2c2e0276752` | Make the quotation route middleware activatable and align the production SEO route inventory after quotation became a noindex utility | `Legacy.Maliev.Web` | `0950ac1` | Public constructor/invoker reflection contract included; Release build: 0 warnings/errors; focused quotation, sitemap, and SEO suite: 30/30; production SEO Pester contracts: 32/32 |
| `791126cb1ae2a92774ec72528b84bff4d0d6a499` | Emit browser-resolvable root-relative responsive service image candidate URLs | `Legacy.Maliev.Web` | `73548c8` | Accounted by the target-native responsive-image slice; rendered-route tests resolve the responsive candidates and no tilde-prefixed candidate URLs remain |
| `c317caca8fb60b775bedd6d2830b05518a442c72` | Publish nationwide CNC metadata, a truthful ISO 2768-1 class m reference, and an explicit drawing/engineering-review boundary | `Legacy.Maliev.Web` | `35f8df3` | Release build: 0 warnings/errors; CNC route suite: 23/23; affected SEO/service suite: 76/76; production SEO verifier: 34/34; format clean |
| `f4ec3657106b09cfd90f68e70d5290043586375b` | Expose the knowledge centre through public footer/navigation discovery | `Legacy.Maliev.Web` | `f6c8461` | Release build: 0 warnings/errors; shared-footer route contracts: 3/3; format clean |
| `21e4c05fa5e1b65a77eab155d93ced1cd01fb8d0` | Publish standard anodizing pricing with a THB 1,100 batch minimum, THB 800 handling, 60 sq. in. inclusion, and THB 5/sq. in. excess | `Legacy.Maliev.Web` | `ba2bcfc` | Release build: 0 warnings/errors; CNC service suite: 23/23; old THB 2,500 contract forbidden; format clean |
| `803013d3bc72e694e899e89cc688fc223e881d5b` | Reject malformed Career page-size input with HTTP 400 before invoking the service client | `Legacy.Maliev.Web` | `35d9f17` | Existing .NET 10 fail-closed behavior proved by a new integration regression; Career suite: 7/7; Release build: 0 warnings/errors; format clean |
| `0d002991028cefb6d0b0fb13e1d65dfdeea3f2da` | Lead English 3D-printing search and hero copy with instant FDM/resin pricing while routing industrial processes to engineering review | `Legacy.Maliev.Web` | `1ef2f17` | Release build: 0 warnings/errors; 3D-printing static-SSR/fallback suite: 19/19; final localized metadata and engineering-review section covered; format clean |
| `e101c2bd4c4680c29571496e7842edf635d4ac6a` | Normalize dash-only optional company/tax placeholders and require exactly 13 ASCII digits for a supplied Thai tax ID | `Legacy.Maliev.Web` | `6036b17` | Release build: 0 warnings/errors; affected Instant Quotation suite: 452/452; browser modules: 103/103; npm audit: 0 vulnerabilities; format and secret scan clean |
| `1f1f9aa9e3c9ff907adf80a553c616864a5a9337` | Expose four allowlisted public WebMCP tools without submission, upload, authentication, network, storage, credential, or field-value access | `Legacy.Maliev.Web` | `1929e18` | Release build: 0 warnings/errors; focused/affected .NET integration: 30/30 after merge; browser modules: 107/107; npm audit: 0 vulnerabilities; format clean |
| `602ef7d` | Replace the provisional proof tile with a source-anchored responsive production-part gallery and route-owned interaction | `Legacy.Maliev.Web` | `127a3c8` | Release build: 0 warnings/errors; exact gallery contracts: 3/3; affected static-SSR, fallback, and asset contracts: 8/8; browser modules: 109/109; npm audit: 0 vulnerabilities; format clean |
| `2300a5c` | Correct production-part composition and add the PC-ESD sample | `Legacy.Maliev.Web` | `127a3c8` | Covered by the exact 20-tile, 60-asset ordinal/hash gallery contract and responsive browser-module tests |
| `6a657ad` | Expand the production gallery from the initial proof set to the complete deferred disclosure | `Legacy.Maliev.Web` | `127a3c8` | Twelve deferred tiles, one-way accessible expansion, and deferred responsive source loading are contract-tested |
| `7eaeeb5` | Preserve the approved crop and framing of expanded production samples | `Legacy.Maliev.Web` | `127a3c8` | Final source-derived WebP assets and responsive framing rules are covered by manifest and layout contracts |
| `dd3644f` | Redesign the expanded production-photo Bento grid | `Legacy.Maliev.Web` | `127a3c8` | Final desktop/tablet/mobile Bento CSS and exact assets are migrated; browser module and CSS contracts pass |
| `2c0b97c` | Remove image-corner borders from Bento tiles | `Legacy.Maliev.Web` | `127a3c8` | Final borderless media treatment is asserted by the migrated gallery/CSS contract |
| `2602699` | Rebalance expanded gallery tile spans | `Legacy.Maliev.Web` | `127a3c8` | Final expanded-grid span rules are preserved in the migrated responsive stylesheet |
| `97fb3bb` | Preserve image framing across expanded Bento tiles | `Legacy.Maliev.Web` | `127a3c8` | Final object-position, aspect, and tile framing rules are preserved with responsive image contracts |
| `9b45c15` | Correct mobile 3D service Bento behavior | `Legacy.Maliev.Web` | `127a3c8` | Mobile and sub-360px gallery rules are migrated with reduced-motion and accessibility behavior intact |
| `600e79a` | Preserve service Bento grids at mobile breakpoints | `Legacy.Maliev.Web` | `127a3c8` | Final shared responsive service-page rules are bundled deterministically and covered by asset contracts |
| `19cae9f` | Fill the mobile production-gallery grid without broken gaps | `Legacy.Maliev.Web` | `127a3c8` | Final compact-grid placement is migrated and validated against the 20-tile composition |
| `6de82fd` | Align the Thai 3D-printing search entry, production-proof link, and final responsive gallery framing | `Legacy.Maliev.Web` | `1ef2f17`, `127a3c8`, `09e9d44` | Current bilingual title/hero assertions and gallery contracts pass; broad affected 3D/service/asset suite: 68/68 |
| `c1ae969` | Model authenticated quotation profile ownership without exposing editable trusted identity fields | `Legacy.Maliev.Web` | `fd4849e` | Target-native BFF/profile boundary verified with the authenticated quotation and submission suite: 28/28 |
| `7e2f009` | Prefill authenticated quotation details from the owned customer profile | `Legacy.Maliev.Web` | `fd4849e` | Trusted profile loading and rendered authenticated identity are covered by the 28/28 focused suite |
| `e863a3f` | Complete authenticated quotation profile handling | `Legacy.Maliev.Web` | `fd4849e` | The migrated workflow preserves owned-profile rendering, server authorization, and fail-closed service behavior |
| `371aa2f` | Gate retry on a persisted quotation before profile completion | `Legacy.Maliev.Web` | `fd4849e` | Submission endpoint and review/customer regression contracts pass in the 28/28 focused suite |
| `a86b618` | Format the authenticated quotation profile implementation | `Legacy.Maliev.Web` | `fd4849e` | No independent behavior; the final target-native implementation builds and its focused contracts pass |
| `b2f5e0c` | Preserve the persisted quotation before profile completion | `Legacy.Maliev.Web` | `fd4849e` | Persist-first retry behavior is covered by the migrated submission/review contracts |
| `f591716` | Distill the 3D-printing tolerance comparison | `Legacy.Maliev.Web` | `19b42e1` | Current tolerance section remains present and is covered by `ThreeDimensionalPrintingParityTests` |
| `a6dc2aa` | Compact the 3D-printing tolerance comparison | `Legacy.Maliev.Web` | `19b42e1` | Final compact comparison is retained in the target-native service component |
| `ff01035` | Permit required Google Ads measurement endpoints in the public CSP | `Legacy.Maliev.Web` | `305c0cd`, `88c1f22` | CSP contract explicitly covers Ads scripts, image beacons, and connection endpoints without wildcard directives |
| `8777004` | Return 404 when CareerService reports a missing offer | `Legacy.Maliev.Web` | `d7d5d5f` | Migrated Career detail route distinguishes missing from unavailable and covers missing ID with HTTP 404 |
| `3b8bfe3` | Persist the customer-visible accepted quotation outcome without treating a browser conversion as business acceptance | `Legacy.Maliev.QuotationService` | `0040af0` | Release build: 0 warnings/errors; full QuotationService suite: 98/98; independent review approved accepted/declined transition, deletion, and availability contracts |
| `7ebbc97` | Preserve the accepted quotation outcome through employee workflow transitions and reporting | `Legacy.Maliev.QuotationService` | `0040af0` | Same independently reviewed 98/98 target-native PostgreSQL suite; customer reaccept-after-decline is rejected while employee override remains authorized |
| `460e9c8` | Replace the low-volume injection hero PNG with the final responsive WebP delivery contract | `Legacy.Maliev.Web` | `87dcaa2`, `3763745` | Release build: 0 warnings/errors; affected static-SSR/service/asset suite: 58/58; browser modules: 109/109; source/target asset hashes match; independent review approved |
| `84a0790` | Publish the verified PP injection-molded home card and bilingual source-derived part proof | `Legacy.Maliev.Web` | `87dcaa2`, `3763745` | Exact EN/TH proof copy and responsive assets migrated; intrinsic 1700x925 dimensions covered; deterministic CSS hashes match; npm audit: 0 vulnerabilities |
| `c1e0ef4` | Preserve the final injection and CNC production-proof asset naming and responsive presentation | `Legacy.Maliev.Web` | `87dcaa2`, `3763745`, `cc708f0`, `cadc876` | Both production-proof galleries now retain their source-derived responsive assets, bilingual copy, semantic figures, reduced-motion behavior, and independently reviewed parity contracts |
| `c97aa4c` | Publish the final 19-item CNC production gallery with exact provenance, bilingual copy, and CTA destinations | `Legacy.Maliev.Web` | `cc708f0`, `cadc876` | Exact ordered fixture locks 19 tiles, 11 deferred tiles, 57 source-identical WebP blobs, EN/TH copy, intrinsic dimensions, responsive sources, social links, and quotation/contact CTAs |
| `5b37938` | Preserve the final wide-screen CNC gallery framing | `Legacy.Maliev.Web` | `cc708f0`, `cadc876` | CNC-scoped desktop media-block assertions lock the final 14-column composition without relying on unrelated service rules |
| `df37579` | Preserve CNC gallery framing through tablet and compact breakpoints | `Legacy.Maliev.Web` | `cc708f0`, `cadc876` | Exact tablet, mobile, and sub-360 media-block assertions lock the 12-, 3-, and 2-column layouts; Release build: 0 warnings/errors; focused suite: 39/39; browser modules: 109/109; deterministic asset rebuild clean; npm audit: 0 vulnerabilities; independent review approved |
| `6509356` | Enforce WebP delivery for every reachable public raster-image reference while removing superseded PNG/JPEG/GIF/ICO assets | `Legacy.Maliev.Web` | `965409c` | Fifteen source-derived WebP assets are locked by exact byte length and SHA-256; public source and generated bundles contain no non-WebP raster references; Release build: 0 warnings/errors; affected suite: 79/79; browser modules: 109/109; deterministic asset rebuild: 37/37 unchanged; npm audit: 0 vulnerabilities |
| `f9b8237` | Apply the shared follow-up spacing to the 3D-printing and 3D-scanning process-guidance links | `Legacy.Maliev.Web` | `1dda94c` | The same target-native hook is rendered on CNC, 3D-printing, and 3D-scanning in English and Thai; exact source and generated CSS contracts pass; Release build: 0 warnings/errors; focused suite: 19/19; browser modules: 109/109; deterministic assets: 21/21 unchanged |
| `48f2a26` | Select the 1536-pixel ABS production-part candidate at the final desktop gallery width | `Legacy.Maliev.Web` | `127a3c8`, `1dda94c` | Existing target markup already carries the exact 64vw desktop `sizes` contract and 1536-pixel source candidate; the translated exact-markup regression now freezes it alongside the final gallery contract |
| `c6ac93af03a0dafc506d9570aca96e4aed3b1643` | Add the consent-gated LINE friendship bridge, privacy-bounded quotation stage diagnostics, and source-attributed qualified-outcome reconciliation | `Legacy.Maliev.Web`, `Legacy.Maliev.QuotationService` | `a53b252`, `303f2f1`, `27f340e` | Web Release build: 0 warnings/errors; LINE/service/SEO suite: 78/78; Instant Quotation suite: 495/495. QuotationService Release build: 0 warnings/errors; full PostgreSQL suite: 155/155; format/package/secret checks clean. Direct GA4 credentials/outbox remain deliberately external; the immutable accepted-outcome plus employee-only aggregate readback is the target-native durable boundary. |
| `7b4b2af697207d36a6e7b7784dddefa150193e97` | Publish verified nationwide file/parcel, appointment, onsite scanning/travel, replacement-part ownership, and PIMM installation coverage | `Legacy.Maliev.Web` | `a53b252` | Exact bilingual static-SSR and FAQ/JSON-LD contracts; LINE route/CSP configuration tests; Release build: 0 warnings/errors; focused affected suite: 78/78; format clean. |

## Supporting migration maintenance

| Legacy commit | Outcome | Validation |
| --- | --- | --- |
| `65be64c` | Pin patched `SSH.NET` 2026.0.0 over the vulnerable Testcontainers transitive version | Restore succeeded; NuGet vulnerable-package audit reports none; Release build: 0 warnings/errors |
| `09e9d44` | Replace stale pre-gallery 3D title and hero assertions with the final production contract | Release build: 0 warnings/errors; affected 3D, service-page, and asset suite: 68/68 |

## Classified source commits without a runtime port

| Source commit | Classification | Evidence |
| --- | --- | --- |
| `ffffbdd60daf573dc76038a5200784b530d36c55` | Historical .NET 8 implementation/deployment plan; no runtime behavior to migrate. Its approved nationwide-title outcome is tracked separately by the later implementation commit `d173d0b`. | Source commit changes only two planning documents and explicitly contains source-repository deployment and Search Console execution steps that must not be copied into the .NET 10 runtime repository. |
| `95edbfbadda5a5e201733c9a83fb6b1eeea73df2` | Source-only .NET 8 Playwright fixture stabilization; no compatible file exists in the migrated test architecture. | The source change manages the .NET 8 executable, jQuery/development vendor copies, and browser contexts. Legacy Web removed jQuery and uses .NET 10 static-SSR integration plus deterministic Node browser-module contracts; copying the fixture would restore eliminated dependencies rather than preserve runtime behavior. |
| `80dd3df1d4de30586c6ecd84b848f4236eade3db` | Historical design document for the service-ownership SEO release; runtime outcomes are tracked commit-by-commit in the following implementation slices. | Source commit changes only a design document and includes source-release operations that are not runtime artifacts. |
| `dc534658f3d7d3271b2788e99517372ca787bf0e` | Historical .NET 8 implementation/deployment plan for the same SEO release; runtime outcomes are tracked by `e36435a`, `30a22af`, `7b1a8fd`, `c317cac`, and related implementation commits. | Source commit changes only a plan containing Razor Pages, source deployment, GKE rollout, and Search Console execution instructions; copying it would misstate the .NET 10 migration workflow. |
| `258ed4aef122737511e27c8aac35130b59d4b30e` | Generated .NET 8 XML documentation artifact; no runtime or compatible tracked artifact to port. | The target does not track a root generated documentation XML file. The meaningful middleware XML comments are present in the .NET 10 source and the Release build remains warning-free. |
| `1fd86b69736bcb078eea1676f94676f687d12d86` | Merge-only integration of `791126c` and `d690804`; no independent runtime change remains to port. | Both parent outcomes are already accounted by Legacy commits `73548c8` and `0950ac1`; the merge commit has no additional non-parent patch to reproduce. |
| `fecf680446c29d926386c14738fbf0e60c911638` | Legacy Razor/CSS alignment fix for the removed material-card info button; architecture-equivalent behavior is owned by the Blazor workflow. | The target has no `.iq-mat-info` or `.iq-mat-title-row` UI. Material choice and comparison are rendered by `InstantQuotationWorkflow.razor` using a native select and details disclosure, so copying the source CSS would be unreachable dead code. |
| `73f045c0f950953bc1ef9b8397e1a16d05dbe543` | Missing email-confirmation input is already rejected by the migrated server routes with a stricter HTTP 400 boundary. | Both the Blazor static-SSR route and retained Razor fallback require non-empty email and token before invoking AuthService. Focused confirmation and surface contracts pass 8/8. |
| `97b6a5d` | Superseded by the final privacy-reviewed quotation analytics contract; do not port `journey_id` or the broader joinable browser payload. | Target exact-schema tests permit only the #153 allowlists and keep internal correlation identifiers out of browser analytics. |
| `e6701e7` | Rejected: consent does not authorize emitting Identity-derived GA4 `user_id`. | The final contract forbids user, session, credential, and authentication identifiers; target consent gating does not alter event payloads. |
| `a318dc6` | Superseded: do not port `maliev_analytics_ready` or a User-ID-dependent initial page view. | The target loads GTM only after the four-field consent update, providing stricter ordering without identity emission. |
| `0712180` | Historical authenticated-quotation design document; runtime outcomes are represented by the target-native BFF/profile implementation. | No compatible runtime artifact to copy; source deployment instructions are outside the Legacy architecture. |
| `e04164e` | Historical implementation plan for authenticated quotation profiles. | Required behavior is covered by target commit `fd4849e` and the 28/28 authenticated quotation/retry suite. |
| `e05680d` | Source reference-document refresh with no independent runtime behavior. | Final profile/retry behavior is represented by the migrated implementation and tests. |
| `2f5c07d` | Source-only local customer fixture; do not port production-facing fixture data. | Aspire/local PostgreSQL test data is owned by the isolated migration/test topology, not public Web source. |
| `a26fac7` | Source deployment-script asset gate; not compatible with the Legacy reusable workflow and deterministic asset pipeline. | Legacy assets are rebuilt deterministically and validated by repository-native CI contracts rather than copying the source deploy script. |
| `b9476b9` | Source packaged-asset deployment validation; architecture-equivalent checks live in Legacy CI. | The target build and browser asset contracts validate generated bundles without inheriting the old deployment entry point. |
| `92e8ad5` | Source shell-removal maintenance for its deployment validator; no target runtime change. | Legacy uses its separate public workflow/reusable-action architecture. |
| `bf3b8d7` | Source removal of the retired Popper v1 path is already satisfied. | The target bundles Bootstrap's supported Popper v2 dependency and contains no legacy Popper v1 loader path. |
| `acdeb8d` | Selectively superseded by the approved target typography contract. Only its service follow-up spacing dependency is portable and is migrated by `1dda94c`; its font replacement is not. | Legacy Web deliberately retains the owner-approved self-hosted `Inter, Noto Sans Thai, sans-serif` stack and matching delivery tests. Importing the source IBM Plex Latin experiment would reverse that explicit migration decision. |
| `63e5f99f3cef37b1f005b3399333ede53e560587` | Source audit-document clarification; no independent runtime artifact. Its confirmed service boundaries are implemented by the following `7b4b2af` runtime slice. | The commit changes only the source audit document. Target runtime copy, links, FAQ schema, and tests are accounted by Legacy Web commit `9f16884`. |

## Classification complete through the inspected source head

All 126 committed source changes after `48e628c` and through source commit
`25418c95b5ac79400029ce274541f0e51728da3e` have now been classified. The final
54-change audit found 21 migrated or architecture-equivalent outcomes, 30 superseded,
merge-only, generated, planning, deployment-only, or deliberately rejected outcomes,
and three missing runtime outcomes. Those three runtime gaps were completed by the
coordinated Web, AuthService, CustomerService, OrderService, and AppHost migration in
PR #179 and its service-side companion pull requests.

The completed target-native flow preserves the persisted quotation before customer
profile completion, verifies credentials on every identity replay, provisions the owned
customer profile atomically, creates replay-safe orders and file links, resumes from a
Data Protection-protected fulfillment checkpoint, and keeps notification delivery
idempotent. It does not reintroduce the rejected identity-bearing analytics payloads or
source deployment scripts.

Source commits `4533669` and `25418c9` add and harden the original UploadService
Kubernetes Workload Identity and storage-authorization boundary. Their runtime behavior
is represented by the Legacy FileService fail-closed storage, signed-URL rollback, and
authorization contracts. The future production Deployment must select the existing
isolated `legacy-maliev-file` service account and pass the GitOps binding gate; this is a
release/deployment gate, not an unclassified source-parity gap, and deployment remains
unauthorized during Aspire review.

Legacy Web main commit `895b866` includes the final parity integration and a bounded
test-host lifecycle. Release build completed with zero warnings and zero errors, the
focused mapping regression passed 6/6 on Windows and Linux path forms, and the complete
suite passed 1,459/1,459 with a measured peak working set of 4.26 GB instead of the
previous greater-than-13-GB failure mode. Formatting, package vulnerability, secret,
browser-module, and deterministic-asset gates are clean.

The 2026-08-30 delta adds three commits after the earlier 126-change checkpoint. All three are
now classified: `c6ac93a` is migrated through target-native Web and QuotationService boundaries,
`63e5f99` is documentation-only with its asserted behavior represented by the following runtime
commit, and `7b4b2af` is migrated into bilingual static SSR, structured data, and tests. The
employee-only quotation readback exposes aggregate attributed/unattributed counts but never source
identifiers or PII, and Legacy QuotationService does not regain direct Measurement Protocol secret
ownership.

The daily automation must resume from the source head recorded above and classify only
new committed source changes. Uncommitted source artifacts remain excluded. Any future
source delta must be migrated or explicitly classified with evidence before the Legacy
release gate can advance.

## Source parity through 4486f0e

The 2026-09-05 inspection extends the immutable source boundary from `7b4b2af` through
`4486f0e964e508e5eb7b43a59eeaec46cc052c67`. The 30-commit delta has ordered sequence
hash `4b803623fe8b3c7592bcfa1559b0476e86eeadf7bd222e8fb583c40fdbe38523`.
Every committed source outcome in that interval is classified in
`source-parity-delta-through-4486f0e.json`; uncommitted source files remain excluded.

Runtime outcomes are preserved by protected Legacy main commits
`1d58887f44c5d0300bb1c24d05d89c4a68f74bfa` (Web),
`887ba566db3468e62d4d027a2fd7196dcecd7a5f` (AccountingService),
`6e3bb55f5ff3ee2b69dd6b4aee6333777ba0ed36` (Workflows), and
`736eeb74f53e9f8c58b1d0f5ddabf01d124262ea` (Intranet). These cover service-page search intent and responsive
presentation; scan comparison and preparation; Instant Quotation review, thumbnail,
CAD-edge, validation, and technical-filament pricing behavior; legal and error surfaces;
strict page-size and address validation; persisted paid-invoice attribution; aggregate-only
receipt validation; and employee-session outcome readback. Documentation-only commits and
source-specific browser fixtures are explicitly classified rather than copied into the
.NET 10 runtime.

Validation completed with zero-warning Release builds, full affected repository suites
(Web 1,581/1,581 .NET and 135/135 Node; AccountingService 65/65 plus PostgreSQL 18
integration; Workflows 53/53 .NET, 13/13 Python, and 7/7 fixtures; Intranet 1,078/1,078
unit and 92/92 browser tests), formatting and package checks, protected-branch CI, and the
exact source-history verifier against the read-only source repository. No Legacy
application deployment, traffic change, production database mutation, or cutover was
performed by this parity checkpoint.

## Incremental source commit fd37fbd (2026-09-26)

Source `fd37fbd03bc767f026a625fd6a91107ada91a96f` (parent
`eb52167166e12d832ea6c800795a8357d526f759`) changes
`Maliev.Web/Areas/Member/Pages/Orders/View.cshtml.cs` and adds
`Maliev.Web.Tests/MemberOrderMissingMaterialTests.cs`. It skips material
lookup when an owned order has no material ID and skips the dependent group
lookup when a material cannot be found.

The architecture-equivalent Legacy owner is `Legacy.Maliev.Web`:
`CustomerMemberDetailClient.GetOrderSupplementAsync` omits the catalog call
for a null material ID, treats a 404 material as absent, and reads the group
only from a present material response. `MemberDetailLoaders` renders both
missing labels as `-` while keeping the owned order visible. Focused tests
verify both null-ID and 404 paths, no dependent group request, no spurious
warning, and the display fallback. Release build: 0 warnings/errors; focused
client tests: 5 passed; full Web suite: 2,149 passed; test-project formatting
passed. GitHub issue #313 and Project #2 track the target PR and post-merge
main SHA. This entry is not a complete checkpoint for every newer source
commit after `7c416cc8cfd27ef7440e7c046529011630276807`.

## 2026-09-26 incremental 3D-printing route parity

Source commit `eb52167166e12d832ea6c800795a8357d526f759` (parent
`1e45a63becfc696459fc6527ef4c85980fb059cc`) changes
`Maliev.Web/Pages/Services/3D-Printing.cshtml`,
`Maliev.Web/wwwroot/src/app/css/service-pages.css`, related source tests, and a
source-only plan. It owns the bilingual instant-FDM/resin versus manual
industrial/metal quotation choice, detailed engineering-review guidance,
responsive cards, and route-tracking attributes. Legacy owner: Web issue #315.
The target adapts these to the static-SSR Blazor component and the existing
`/InstantQuotation/3D-Printing` and `/Quotation?item=3D-Printing` routes while
preserving its additional accepted CAD formats. This entry classifies only this
commit; it is not a claim that every later source commit has been reconciled.

## Incremental source commit 1e45a63 (2026-09-26)

Source `1e45a63becfc696459fc6527ef4c85980fb059cc` (parent
`4bde312241c2e063e6768f510b92ee2f60b2b94b`) changes
`Maliev.Web/Pages/Services/CNC-Machining.cshtml` and
`Maliev.Web.Tests/CncQuotationRouteSourceTests.cs`. When instant CNC quoting is
disabled, its primary CTA must route to manual engineering quotation rather
than advertise the unavailable instant flow. Legacy Web already chose the
manual `/Quotation?item=CNC-Machining` target; issue #316 adds the source's
explicit manual-CTA marker and bilingual regression assertions that the
unavailable `/InstantQuotation/CNC-Machining` link is absent. CNC expansion
remains separate. This classifies only this source commit, not the entire
source delta since the prior full checkpoint.

## Incremental source commit 63d68317 (2026-09-26)

Source `63d68317f7ae12def6b7caceca9883a7b75475df` (parent
`915a880f13a9aa6ec7034d13dc986ec46ae11488`) changes the original
additive-simulation worker, its JavaScript regression, and the original Web
worker URL cache version. Legacy owner: Web issue #319. The retained Legacy
additive worker had the same rounded-XY contour collision; its section keys
now use shared 3D mesh edge/vertex identity. A nearby-closed-solids regression
fails before the fix and passes afterward; open meshes still fail review.
The original `SecurityHeaderPolicy` URL pin has no direct Legacy analogue:
the current Blazor quotation route does not load this worker URL, and no
production asset points to it. This source commit is classified here without
claiming that the retained JavaScript worker is the active Legacy pricing path
or that the wider source delta is complete.

## Incremental 3D-printing CTR route and decision parity (2026-09-26)

This bounded Web checkpoint accounts for five committed source changes on
`R:\maliev-web` `origin/main`. Legacy Web issues [#322](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/322)
and [#323](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/323) are
closed by the protected-main merges below. It does not classify unrelated
newer source commits or authorize deployment, metadata publication, or a
GTM/Ads change.

| Source SHA | Individual disposition in Legacy Web |
| --- | --- |
| `a73acf2e4de9a611c7eda22cf6dbdae333d231cf` | Source implementation plan, not a runtime artifact. Its Web route/measurement slice is tracked by #322 and merged [PR #324](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/pull/324) at `bd1a2b01a09c177b8aca290d26dc5a1ae22b675d`; its Web CTR decision slice is tracked by #323 and merged [PR #325](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/pull/325) at `fd9ae45d94ed28c7288085cb6ce543b5851a26d5`. The plan also names QuotationService #52 and Intranet #177, whose acceptance is outside this Web checkpoint. |
| `c97ced90bd8913a1686fec16405efd57c62d3176` | Route-choice attributes, consent-safe diagnostic event, and source browser behavior migrated to the Blazor static-SSR page, route-owned JavaScript bundle, and English/Thai browser tests by Web #322 / PR #324 (`bd1a2b01a09c177b8aca290d26dc5a1ae22b675d`). The instant and engineering destinations remain distinct; this event is intent, not a persisted lead. |
| `31ba7d7c9816330529c8c335939fde5d0fc4a632` | Dated CTR decision artifact migrated under Web #323 / PR #325 (`fd9ae45d94ed28c7288085cb6ce543b5851a26d5`). The canonical owner, verified-fact versus hypothesis boundary, and mature-window/owner-approval gate remain explicit; the proposed snippet is not published. |
| `de75a0e55240ee78053c8c87eb4a83b67cdd32f2` | The Web browser-test formatting and behavior are represented by Web #322 / PR #324 (`bd1a2b01a09c177b8aca290d26dc5a1ae22b675d`). This source commit also formats an Intranet test; that independent portion is outside this Web checkpoint. |
| `0665dcd54788c037ee663ff90f32741014f0c81c` | Decision-pack whitespace normalization is represented in the migrated final document under Web #323 / PR #325 (`fd9ae45d94ed28c7288085cb6ce543b5851a26d5`); it has no independent runtime effect. |

Both merge commits are ancestors of current Legacy Web `origin/main`
`fd9ae45d94ed28c7288085cb6ce543b5851a26d5`. Independent acceptance on
that main SHA: Release build of `Legacy.Maliev.Web.Tests.csproj` succeeded with
zero warnings and errors; the focused 3D-printing rendered-route, decision-pack,
and real headless-Chromium English/Thai tests passed 9/9. They cover ten labeled
choices at five placements, both destinations, no pre-consent `dataLayer`
route event, one buffered scalar event per click released on consent despite
reloading the handler, and no lead event. These checks do not establish
production deployment, GA4/GTM delivery, persisted quotation, or
qualified-customer outcomes.

## Instant Quotation review actions and calculating status (2026-09-26)

This bounded #276 checkpoint maps two committed `R:\maliev-web` source SHAs to
the target-native Blazor workflow. It does not reopen the already merged action
change or claim parity for the other source SHAs in #276.

| Source SHA | Legacy Web disposition |
| --- | --- |
| `ce8a2f4037bedf51df1aedde4aef531c2faff3c7` | Already migrated by [PR #312](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/pull/312), merged on protected main at `62cc8a1f26b09942af874205d3df13f819c65b26`. `InstantQuotationReview.razor` preserves PDF-before-Continue order; the compact right-aligned action row in `instant-quotation.css` keeps 44px controls, wraps long labels, and exposes keyboard focus. This checkpoint verifies existing parity without duplicate changes. |
| `d46b4d6a3fca8148792da1033c77d60d22d7b9d9` | Adapted by Legacy Web commit `e522fa5` with a subsequent state-transition hardening commit: repricing shows a localized heading-adjacent status and reduced-motion-safe spinner while retaining the previous authoritative total; the totals alone carry `aria-busy`. Start/finally render notifications clear the status after success or failure, including overlapping updates. The source's physical-simulation report is not copied because Legacy Web has no corresponding separately refreshed simulation report; its quote refresh supplies price and lead time through the coordinator. Browser geometry evidence uses representative review DOM, not a production-derived quotation. |

This is source-behavior parity, not a source-shape port. No deployment, service
write, or production quotation was performed. #276 remains open for its other
source commits and final acceptance. The final slice passed a Release
build with zero warnings/errors, 20 focused source/state/browser tests at
compact, tablet, and desktop widths in light/dark where relevant, and the full
Web suite (2,169/2,169). An earlier full-suite rerun was needed because an existing
820px bulk-savings CSS assertion failed once, then passed in isolation and
again in the complete suite; this remains an intermittent-test watch item.

## #276 remaining-source audit and customer checkbox (2026-09-26)

This checkpoint started from protected Legacy Web main
`3d95302924ca7f27685d406ec3ec9168a24019b6` and inspected the committed
source objects listed in #276. The previously reconciled `3f090e9`, `ce8a2f4`,
and `d46b4d6` lanes remain unchanged. The table classifies each other full
source SHA without treating source-only tests or absent source-specific DOM as
new production requirements.

| Source SHA | Disposition at this checkpoint |
| --- | --- |
| `03254079a98245509e73a41e143ff75bf536449b` | Target-native review fields, DFM verdict disclosure, and PDF/Continue action row are present in `InstantQuotationReview.razor` and covered by `InstantQuotationReviewLayoutBrowserTests`. This audit did not change them; final uploaded-part review acceptance remains open. |
| `7f010ba7742e96367bdf5039dd03e994fadcfa1b` | Account email/token messages and Thai translations already exist in the target. Instant Quotation projects server validation to controlled invalid-field names and localized Blazor field guidance rather than exposing source Razor validation text. Exact bilingual submission/error-state acceptance remains open; raw source messages were not copied. |
| `49294cf81ec1940c433d9092af0b96f050298930` | A real cascade gap was found and fixed by Legacy Web `8ff01cc`: the shared workflow input's 44px minimum height overrode the checkbox's nominal 20px height. The scoped CSS override keeps the native checkmark compact while its associated label remains at least 44px tall. `InstantQuotationCustomerCheckboxBrowserTests` now inserts the fixture inside the actual workflow and verifies EN/TH, 320/375/820/1280px, label click, keyboard Space, and no horizontal overflow. |
| `7aa95d1111528d3d875d8001a43676118974c1d0` | Source-specific physical-simulation report has no separately refreshed Legacy Web counterpart. The target repricing status already uses `min-width: 0` and `overflow-wrap: anywhere`; no obsolete simulation DOM was copied. Long-status browser acceptance for the target remains a separate check. |
| `20fe822f80cb8f04d671897281a96a9c7dc33510` | Source test-only change opens the collapsed source summary before asserting details. Target `InstantQuotationCompactSummaryBrowserTests` already opens native `<details>` before testing visible totals and controls. Deeper target detail assertions remain a separate test-coverage opportunity. |
| `b6f9eacea2669c7fcf0f619dc0c96f2f104ca861` | Source test-only selector update for a metrics wrapper. The target uses its own semantic summary dock and does not have that source wrapper; no runtime port applies. |
| `027e733e8e7a5abebf975598fda86589ca034b32` | Legacy Web `5a489b5` completes the applicable native summary-dock behavior: the collapsed dock retains its total and Review control, a badge reuses the configuration DFM warning predicate, expanded content includes part geometry/pricing and privacy/NDA links, and a separate localized consultation link opens the existing contact form in a new tab without losing the quote. This maps the source consultation door to the target's existing route rather than copying its channel menu or JS state machine. `InstantQuotationCompactSummaryBrowserTests` verifies keyboard expansion, warning semantics, legal/detail visibility, 44px consultation control, focus, and overflow in Chromium at 320/375/820/1280px across EN/TH. No service contract or CNC behavior changed. |

The checkbox slice passed a Release build with zero warnings/errors, focused
5/5 real-Chromium source/geometry/keyboard checks, full affected Web suite
2,172/2,172, deterministic asset build, format, and npm/NuGet vulnerability
checks. The browser fixture does not constitute an uploaded-part or production
quotation. No deployment or service write was performed.

The `027e733` summary-dock slice passed a Release build with zero warnings/errors,
focused real-Chromium checks 8/8, the affected Web suite 2,177/2,177,
deterministic assets, formatting, npm/NuGet vulnerability audits, and staged
gitleaks. Browser tests use representative configuration DOM with production
CSS and source-contract assertions; they do not claim an uploaded-part or
production quotation. No deployment or service write was performed. Keep #276
open for the remaining source SHAs and broader acceptance.

## #276 additive review acceptance checkpoint (2026-09-27)

Starting from protected main `f117b3a0070bf7a32e8d8c0736b9b0a15c2dc1d7`, the
committed source objects were rechecked through `4198baa6b0e7903f2b9b6e3d5d68f9d2c2b5b0db`.
This slice strengthens the target-native review browser fixture for Thai and
English, light and dark scheme, 320/390/820/1522 px layout, collapsed DFM
warning access, keyboard disclosure/focus, touch disclosure, and forward-action
reachability. The fixture uses representative review DOM and production CSS; it
does not establish uploaded-part or production-data parity.

| Source SHA | Current disposition |
| --- | --- |
| `03254079a98245509e73a41e143ff75bf536449b` | Review editing, DFM verdict, and responsive PDF/Continue behavior were adapted by merged PR #306 and #312; this checkpoint adds cross-culture/theme/interaction regression, not a second runtime port. Uploaded-part recalculation remains unproven. |
| `7f010ba7742e96367bdf5039dd03e994fadcfa1b` | Account and quotation field localization is target-native as described above; bilingual submission/error-state acceptance remains open. |
| `3f090e9488d91790636558d3ce59c7f046efa1d8` | Compact pricing control reachability was merged by PR #329; completed-pricing fixture is not an uploaded quotation. |
| `ce8a2f4037bedf51df1aedde4aef531c2faff3c7` | Review quotation actions were merged by PR #312. |
| `d46b4d6a3fca8148792da1033c77d60d22d7b9d9` | Localized repricing status was merged by PR #330; its geometry fixture is not a production-derived quote. |
| `49294cf81ec1940c433d9092af0b96f050298930` | Customer checkbox sizing and keyboard/touch regression were merged by PR #331. |
| `7aa95d1111528d3d875d8001a43676118974c1d0` | Source-specific simulation DOM has no target counterpart; target status containment is present, while long-status and uploaded-quote acceptance remain open. |
| `20fe822f80cb8f04d671897281a96a9c7dc33510`, `b6f9eacea2669c7fcf0f619dc0c96f2f104ca861` | Source test-selector changes; native dock tests open their own semantic details before asserting content. |
| `027e733e8e7a5abebf975598fda86589ca034b32`, `663dd8c9c36eece39cd2f641895027ac926e1183` | Compact dock behavior is represented by merged PR #293 and #332; source channel-menu JS is intentionally not copied. |
| `57654d9ecea01c99b947e48bc759fb9a37dcf388` | Editable review layout is represented by PR #306, and quote-matched bulk savings by merged PR #307; do not infer new discount tiers from the source. |
| `c88a4e93c28b7306a5f2c353ee2ed1b8677ff7f8` | Human-readable preliminary print durations were merged by PR #289. |
| `4d9954c086c391698399b9a089f64dd57fbf5a95` | Source merge object combines unrelated consent changes; no separate additive runtime port applies. |
