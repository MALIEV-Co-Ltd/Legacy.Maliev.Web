# Additive quotation evidence and decisions — 2026-09-19

This record separates facts observed on the MALIEV workstation and live Service Pricing workbook from policy decisions and still-blocked operational evidence. It covers GitHub issues #55–#64. Customer CAD is not committed.

## Verified local evidence

| Evidence | SHA-256 / version | Status |
| --- | --- | --- |
| `280te.stl` and its two duplicate copies | `C02B07FBA97650DC91671C6BEF0E622BE3F07F9F3F04E18FC0475BA9B92007CB` | Source digest verified; benchmark consent and project 3MF remain missing |
| `Body4.stl` | `6BFA348211321F26EFB5FA1BED9AC68EC3810261F765617DC3E67B45CF8B3569` | Previously reported missing source was found; benchmark consent and project 3MF remain missing |
| Bambu Studio | `2.8.2.61` | Installed workstation version observed; not an approved server runtime |
| X1C 0.4 mm machine profile | `E2B26B1E94216C8C1D823B9A327BF746C0AEEB1FA0E61489846CFC15B96B3B53` | Installed Bambu system profile approved by the owner for quotation evidence |
| X1C 0.12 mm High Quality process | `2DE5E475BDEC6A19B9BFB76EB70F99E2C69E1957AF9E245E6C818F8B9F599EB7` | Owner-approved quality choice; resolved inheritance is hashed by the harness |
| X1C 0.20 mm Standard process | `E4D3F211B6EF87E9612C96F3AC2D18413E0610FFF7EEB815A76420344A3B0A19` | Owner-approved standard choice; resolved inheritance is hashed by the harness |
| eSUN ASA user filament profile | `7B2C02C20DE7A72EFA00BC9465430753C449FBA4AA683422FF5D7005B64C9215` | Observed local profile; no proof it produced the reference slice |
| eSUN PLA user filament profile | `87B17C4ECD42DD231A53F65F66404CFC015AB4756B972B9C34C264D0CAF7B598` | Observed local profile; no proof it produced the reference slice |
| Resolved Body4 profile bundle | `0799329B33562DA119EFFDF60D169A0328F29583B42390F973C4962F95DD0235` | X1C 0.4 + 0.20 Standard + PolyTerra PLA; approved profile choice, measured production actuals unavailable |
| Resolved 280te profile bundle | `A2EE3222942D43E67B6456B48FB2C5F1421448EEC87BDA93FAC866E79BA3CD25` | X1C 0.4 + 0.12 High Quality + PolyLite ASA; approved profile choice, measured production actuals unavailable |

Bambu Studio's official CLI accepts machine/process settings through `--load-settings`, filament settings through `--load-filaments`, and can slice/export a 3MF. Local execution found two Windows-specific correctness traps: `--orient` requires an explicit value despite the wiki example, and selecting nonexistent STL plate 2 can return code zero without `sliced_plates`. The harness now resolves the installed inheritance chains, uses all-plate slicing, requires an explicit bed, and rejects empty success responses. Its upstream project is AGPL-3.0. A production worker remains disabled until the resolved bundles, sandbox design, and packaging/license review are approved.

The resolved Body4 run reproduced the supplied reference: 207.38597 g, 13,282.029 model seconds, and 13,746.885 total seconds (3 h 49 m 7 s). The resolved 280te run produced 9.78885 g, 2,700.962 model seconds, and 3,281.096 total seconds (54 m 41 s). The latter differs from the earlier screenshot (9.66 g, 51 m 46 s), so it is recorded as slicer/profile version drift rather than silently replacing the commercial workbook input.

## Supported filament profile coverage

`filament-profile-catalog.v1.json` records a decision for all 19 FDM material keys exposed by `PricingCatalog`. It uses Bambu Studio 02.08.02.61 resolved inheritance rather than copying leaf presets into the repository. The owner approved the built-in X1C machine profile and `0.12mm High Quality`, `0.20mm Standard`, and `0.20mm Strength` process profiles. Official Polymaker presets remain the preferred evidence for stocked Polymaker products. The operator-provided `Z:\bambulab x1c esun` exports now resolve exact eSUN PLA-CF, PETG-CF, ABS-FR, PC, PC-ESD, PA6, and TPU-95A candidates. Duplicate local preset names are accepted only when their bytes are identical.

| Coverage state | MALIEV materials | Decision |
| --- | --- | --- |
| Approved official Polymaker/Bambu profile | PLA, PETG, PETG-ESD, PET-CF, ABS, ASA, ASA-CF, HIPS, PC-FR, PA-CF | Resolved and hashed; eligible when the isolated worker is activated |
| Approved operator-local eSUN profile | PLA-CF, PETG-CF, ABS-FR, PC, PC-ESD, PA6, TPU-95A, PVA | Resolved and hashed; grade questions for PA6 and TPU-95A were answered by the owner |
| Conservative substitution | PA12 | Generic PA profile is approved for preliminary analytical pricing; the exact-vendor PA12 slicer path stays review-only |

Community profile repositories and vendor parameter sheets are useful discovery evidence but are not promoted directly to production profiles. Every selected preset must resolve to a full immutable JSON digest, match the stocked SKU/grade, and receive operator approval plus measured production references. The validator prevents `automationEligible=true` unless those exact-match and approval conditions are true.

## Generic resin evidence

The owner authorized conservative research-backed resin pricing because measured job data is unavailable. The provisional profile is `generic-msla-mighty4k-aqua-gray-2026-09-19.v1` and remains labelled `research_provisional` with mandatory engineer review. It uses Phrozen's official Sonic Mighty 4K envelope (200 x 125 x 220 mm) and Aqua-Gray profile: 0.05 mm layers, six bottom layers at 32.5 seconds, normal exposure 2.3 seconds, 8 mm lift at 60 mm/min, and 8 mm retract at 150 mm/min. The estimator now includes exposure, lift, retract, and bottom-layer exposure instead of the former 2.5-second exposure-only constant. Existing 30-minute per-part post-processing labour, 100 THB consumables, 15% resin allowance, 15% reserve, 30% margin, minimum price, fee, and VAT keep the preliminary quote commercially conservative.

Sources: Phrozen's official [Sonic Mighty 4K resin profile](https://info.phrozen3d.com/pages/resin-sonic-mighty-4k), [printer specifications](https://eu.phrozen3d.com/products/sonic-mighty-4k), and [post-processing guide](https://helpcenter.phrozen3d.com/hc/en-us/articles/6571918778265--Sonic-Mighty-4K-Post-processing-guide). Polymaker profile discovery uses the official [preset downloader](https://presets.polymaker.com/) and [PA profile catalogue](https://wiki.polymaker.com/polymaker-products/printer-profiles/legacy-profiles-by-material/nylon-pa).

## Live workbook policy decision

The live `Service Pricing` workbook is the commercial source of truth until a versioned replacement is approved. For FDM, keep this order:

1. Direct material plus machine and allocated overhead cost.
2. Complexity factor.
3. Quantity-tier target margin and discount.
4. Minimum/base-order ceiling to THB 5.
5. Setup/admin once per order.
6. Named commercial reprint reserve; do not describe it as a measured failure probability.
7. Packaging once per order plus any separately priced delivery.
8. Rush multiplier, payment-fee gross-up, VAT, then final ceiling to THB 5.

The current tier table remains policy: quantity 1/10/50/100, discount 0%/5%/10%/15%, target margin 50%/35%/25%/20%. Resin keeps its separate 30% workbook margin until approved production cycle and cost records support a replacement. The 280te parity case is ASA, 10 g, 51 minutes, quantity 1, and THB 755 gross. Body4 is PLA, 207.39 g, 229 minutes, quantity 1, and THB 3,490 gross. Exact slicer outputs and rounded workbook inputs are deliberately separate evidence.

## Issue disposition

| Issue | Decision and current boundary |
| --- | --- |
| #55 benchmark | Harness/schema/report is releasable. Source digests improved. Certification remains blocked by consented corpus, project 3MF/profile exports, and production actuals tracked in #64. |
| #56 content binding | Server upload receipts now bind session, immutable upload ID/path, server-computed SHA-256, and analysis revision into line ticket v2 and reject malformed geometry. Keep open only for server-side physical analysis and durable cross-service snapshot storage; browser geometry remains explicitly provisional. |
| #57 costing | Workbook sequence is active in order ticket v2. Base, minimum, setup, reserve, packaging/delivery, fee, VAT, rounding, and total are persisted and displayed separately. ASA 755 and Body4 3490 parity tests pass. |
| #58 FDM slice worker | Preset resolver, all-material profile catalog, verified Windows argument contract, and fail-closed result parser are ready. Keep production execution disabled pending durable #56 jobs, sandbox/container work, license packaging review, and approved profile artifacts. |
| #59 resin | Conservative generic 405 nm MSLA cycle is active for preliminary pricing with mandatory engineer review; it is not represented as an operator-tuned production profile. |
| #60 DFM | Every browser-derived estimate persists provisional confidence and mandatory engineer-review state. Generic topology/size warnings remain; material-specific automatic blockers remain deliberately excluded without labeled holdout evidence. |
| #61 outcome and lead time | Server order receipts calculate occupied days from verified line minutes at 24 hours/day and add the approved two-calendar-day customer buffer; the range is persisted and displayed. There is no queue feed. |
| #62 measurement | Legacy receipts now retain upload/profile/policy/cost evidence. Paid/refund and realized-margin reconciliation is explicitly deferred to the future .NET 10 `Legacy.Maliev.*` payment integration; bank-transfer and Thai QR entries are not inferred as settled allocations. |
| #63 overview | Remains open until the dependent implementation issues and operational gates are complete. |
| #64 operations | Body4/280te were reproduced, all 19 FDM keys have an approved exact or explicit conservative substitution, the eSUN disputed grades are resolved, generic resin evidence is versioned, and the owner supplied the 24/7/no-queue capacity policy. Measured production jobs remain unavailable by owner confirmation and are not fabricated. |

## Release rule

The benchmark harness remains fail-closed for statistical certification: exit code `2` is expected until a representative measured corpus exists. Customer estimates therefore remain preliminary and engineer-reviewed. The approved research resin cycle and 24/7 lead-time policy are product decisions, not claims of measured production accuracy. The isolated Bambu worker stays disabled until its production runtime and packaging/license review are complete.

