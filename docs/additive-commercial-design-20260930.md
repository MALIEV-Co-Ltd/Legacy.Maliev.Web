# Additive commercial economics design

## Preparation state and release gate

Workspace: `web-additive-commercial-bundle-20260930`, branch
`codex/additive-commercial-bundle-20260930`, base
`4483e3eb768e8ee59a4a2cf6ba9e2f452e0f3ed0`.

Preparation does not authorize implementation. Wait for exact-main run
`36735708564` to succeed and root to release the next gate. No redundant full
baseline is needed: root independently tested the preceding candidate with
2,481 passing tests and confirmed the merged tree identity. After release,
restore/build this private dependency graph (Release zero warnings/errors), run
the baseline calculator/pricing focus, then write and observe intended RED.
Production changes require the observed RED and root design gate.

Root subsequently approved inert internal model scaffolding only: the source-named
minimum/commercial properties retain zero/empty defaults without calculations.
This enables direct-property literal RED tests; it is not runtime feature acceptance.

Observed gates: focused baseline160 passed; expanded direct-property RED30 failed /
3 passed /0 skipped, with Release0 warnings/errors. Root then released only the
calculator, catalog and genuinely quantity-bound physical pricing logic. Core
GREEN33 passed; expanded affected pricing/profile/public-money focus212 passed.
Submission message identity had its own observed assertion RED before root approved
the conditional actual receipt profile version (unknown omitted, policy separate).
Final full/static gate evidence is recorded below; root independent acceptance
remains required and these checks are not deployment/release evidence.

Web CI dependencies are ServiceDefaults
`6ea131df4bcf8d213d7d121cb8c865697bee7420` and CompatibilityContracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. All dependency clones and their outputs
are owned by this workspace under ignored `.dependencies`; Web outputs use its
own `bin`, `obj`, and `TestResults`. Node dependencies use its own `node_modules`.
No other workspace outputs or original repository refs are writable.

## Scope and immutable identities

Apply latest source commercial economics to genuinely admitted physical results.
The source checkpoint is `bed10c7d15e0698e0b75f1329d0f312937f5d77f`; parent tracking
is Web #275 and approved design comment 5914162504.
The bounded implementation/acceptance child is Web #421; parent #275 remains open.
Acceptance of this slice closes neither the parent nor entire source commits.

Use commercial policy `additive-2026-09-30.v10` only when changed economics are
implemented. Manufacturing profile version/digest and physical algorithm/ledger
identity remain separate, actual values. Old v9 monetary tickets must reject;
recalculation issues new tickets rather than relabeling old prices.

Preserve admission identity: session, owner, part/file IDs, immutable upload
SHA256, material, preference, quantity, manufacturing profile version/digest and
physical ledger digest. A whole-quantity ledger is not a one-part ledger. Support
material is already in total deposited volume; its separate grams drive removal
labour, not a second material charge. No geometry-only or unavailable-price fallback.

No worker, upload authority, STEP, CNC, consumer UI, deployment, Workflows or
persistent-data changes. Public offer/certification changes are not automatic
consequences of adopting a density. Revised grade data must match the reviewed
source composition and admitted profile; incompatible or unavailable profiles
remain unavailable, not cross-profile substitutions.

The legacy geometry-only compatibility `QuoteItem` monetary path is deliberately
unchanged. This wave is not complete Web #275 / v10 parity for every historical
calculator or consumer path, and does not disposition separate admission work.

## Commercial kernel and independent goldens

Retain the existing internal calculator boundary. Manufacturing base is
`max(raw margin/discount base, minimum per ordered unit * quantity)`. Apply source
THB5 base ceiling, explicit process-minimum surcharge, weighted reserve,
setup/packaging/delivery, rush, fee gross-up, VAT and THB5 gross ceiling. Commercial
subtotal is rounded gross minus unrounded VAT, delivery and explicit minimum
surcharge; exposed decimal currency uses satang away-from-zero rounding.
Customer unit price is the THB10 upward ceiling of commercial subtotal / quantity.

Independent synthetic inputs: direct cost1, margin50%, discount0, reserve10%,
setup39.0625, packaging20, fee3%, VAT7%, process minimum300.

| Quantity / per-unit floor / delivery | Manufacturing | Reserve | Pre-VAT | VAT | Gross | Commercial subtotal | Customer unit |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 / 500 / 0 | 500 | 50 | 627.90 | 43.95 | 675 | 631.05 | 640 |
| 2 / 500 / 0 | 1000 | 100 | 1194.91 | 83.64 | 1280 | 1196.36 | 600 |
| 5 / 500 / 0 | 2500 | 250 | 2895.94 | 202.72 | 3100 | 2897.28 | 580 |
| 1 / 0 / 0 | 2 | 30 | 401.10 | 28.08 | 430 | 106.92 | 110 |
| 1 / 500 / 100 | 500 | 50 | 730.99 | 51.17 | 785 | 633.83 | 640 |

The commodity calculator surcharge is295 (base300 minus THB5-rounded
manufacturing5), not298. Active order pricing of a rounded110 line, process
minimum300 and delivery100 has surcharge190, pre-VAT400, VAT28 and total428.
Standalone item pricing supplies delivery0; delivery100 above tests only the
calculator's separation of commercial and delivery amounts.

Source workbook anchors: ASA base565/reserve56.50/pre-VAT701.61/VAT49.11/gross755;
second ASA pre-VAT531.51; Body4 base2820/reserve282/gross3490. The actual source
support-heavy TPU ledger has unit670 at quantity1 and580 at quantity5. Do not
confuse it with the synthetic minimum fixture's quantity1 unit640.

Add heterogeneous reserve/line RED: line a has quantity2, direct1, margin50%,
minimum500 and reserve10%; line b has quantity1, direct100, margin50%, minimum0
and reserve20%. With the same charges above: raw204, manufacturing1200,
minimum adjustment996, reserve140, pre-VAT1442.33, VAT100.96, gross1545,
commercial subtotal1444.04. Gross allocations are a1287.50/b257.50; commercial
allocations are a1203.37/b240.67. Permuting input order must preserve these values.
Both allocation collections must sum exactly to their corresponding rounded
public total, including residual-satang cases. Duplicate ordinal stable line IDs
must reject before calculation; a largest-remainder `Contains` check must not
award one residual satang twice to duplicate IDs.

## Materials and tiers

Freeze reviewed catalog densities/costs/classification against source composition
and runtime profiles: TPU1.43, PC-FR1.19, ASA-CF1.02 with drying=true, PETG-CF1.30,
PET-CF1.34, HIPS1.05, PA12 1.012. Preserve current ESD identities and evidence.

Eight commercial tiers use quantities1/10/50/100/500/1000/5000/10000; margins
.50/.35/.25/.20/.18/.17/.16/.15; discounts0/.05/.10/.15 thereafter. Test immediately
below/at each boundary and10000 accepted/10001 rejected. Price rows for another
quantity require correctly bound physical evidence; an arbitrary batch ledger
does not authorize a fabricated bulk table.

High-quantity synthetic monetary goldens use model1cm3/unit, support0, motion60s/unit,
cost1000THB/g, density1, non-drying, waste10%, machine17THB/hour and overhead
311097*.70/43200 THB/minute. Direct cost is1105.3242569444444444444444444.
Setup39.0625, packaging20, reserve10%, discount15%, fee3% and VAT7% feed the
independently calculated decimal sequence. Literal customer unit/subtotals:
499=1340/668660; 500=1300/650000; 999=1300/1298700;
1000=1290/1290000; 4999=1290/6448710; 5000=1270/6350000;
9999=1270/12698730; 10000=1260/12600000. These are deliberately synthetic,
not source-observed manufacturing or calibration evidence. Root approved a
temporary wrong-tier control changing only `ResolveTier(quantity)` to
`ResolveTier(100)` in `QuoteFdmSimulation`, then exact restoration before final
verification. The mutation is not part of the implementation.

## Narrow ownership and acceptance

Production: Application/Pricing/{AdditiveOrderCostCalculator,PricingCatalog,
PricingEngine}.cs, Application/InstantQuotationPricingService.cs, and only required
ticket/submission changes in Web/Services/AdditiveQuoteTicketService.cs and
Application/InstantQuotationSubmissionService.cs. Internal breakdown/model fields
may grow as needed; no new public route or unreviewed DTO contract.

Tests: existing calculator, technical minimum, catalog/profile, pricing/workflow,
ticket and submission tests; literal synthetic physical fixtures must be labelled
synthetic rather than claimed to prove manufacturing calibration. Add monetary
conservation, duplicate IDs, old-ticket rejection, actual profile/policy separation,
support-once, unavailable/stale physical input and source composition compatibility.
Use existing rendered/browser acceptance for unchanged review consumers as needed.

Validation order after gates: private restore; Release0W0E; focused baseline;
intended RED; root design gate; minimal GREEN; Release0W0E; focused/affected/full
suite and coverage; static/manifest/browser-asset checks, audit and secrets;
appropriate real-admission HTTP/ticket/submission acceptance. No commit/push until
root review. Hosted/browsed acceptance and calibration are not implied by pure
calculator tests.

## Individual source cohort and retained dispositions

Remaining economics: `da2796fd4dd395cb2a839057f4e8dfd99f13f6b6`,
`07845568583107d38aab31ee8d1ef87e094ea57c`,
`e5432817078e133604239ebfe2b806465ed8e436`,
`5320f8ea6cf9ffdeb1c3f2bf51c75b3511f525a1`,
`a3c5c4a53907e03ac202620cc051393c20fc28e3`,
`2712f7d05d982ba7a3a2e4905c3ff61073d0f13a`,
`a485be980ae51db01b8f60e671b371b8755755f0`,
`ea0743c0c7e8653462eebe813af6c7a5dbfd8438`,
`cebda874cb1065c3296ec0770bcce7d29932f1d9`.

Individual mapping within this coherent bundle (not whole-commit completion):

- `da2796fd4dd395cb2a839057f4e8dfd99f13f6b6`: retained workbook kernel and literal workbook anchors.
- `07845568583107d38aab31ee8d1ef87e094ea57c`: standalone commercial subtotal feeds rounded customer unit pricing;
  protected order pricing remains the existing separate public boundary.
- `e5432817078e133604239ebfe2b806465ed8e436` / `cebda874cb1065c3296ec0770bcce7d29932f1d9`: reviewed density/classification literals match existing
  qualified composition metadata and actual resolved server profiles. No new
  profile admission or calibration claim follows from this adoption.
- `5320f8ea6cf9ffdeb1c3f2bf51c75b3511f525a1`: retained upward THB10 unit ceiling before quantity multiplication.
- `a3c5c4a53907e03ac202620cc051393c20fc28e3` / `ea0743c0c7e8653462eebe813af6c7a5dbfd8438`: preserve support material counted once and separate removal
  labour; no geometry-authority or support-generation implementation in this wave.
- `2712f7d05d982ba7a3a2e4905c3ff61073d0f13a`: drying-required manufacturing floor is per ordered unit before the
  commercial sequence. Existing genuinely quantity-bound evidence only; no
  cross-material or cross-quantity invented ledger.
- `a485be980ae51db01b8f60e671b371b8755755f0`: adopt all eight contribution-protected economic tiers without
  fabricating unproved bulk physical rows or editing consumer UI.

Historical analytical PET-CF calibration retains its explicit density1.30 and
original inputs/bounds. Current catalog/profile composition tests use density1.34.
Real calibration for that current grade remains separate and is not inferred from
the historical analytical fixture or synthetic monetary ledgers.

Physical/profile prerequisites: `6431b1b649015dc17ad936ed18eb59c1ff7f4080`,
`61e563461973cbe0e3862894ad4431eb9263739d`,
`199e5f94562d3f1621f20c598ae5abef4d6c98d0`,
`49033c72d2270c81eb08ca2621ef7b9defc950e8`.

Keep accepted earlier slices: rounded lines
`f83453b48d1ad9d365de7d4e09d8afc1b4093c3a` (#298), quantity limits
`f0ae0e8f4f71231d5a4cbb9adebbb107323519e2` (#299), savings
`57654d9ecea01c99b947e48bc759fb9a37dcf388` (#303), enclosure energy
`8ec87e40c1104485537823bb5059833f2735cc9e`, manufacturing-unit semantics
`31e8f5d28d11f903687c4e540441b19bbfbfe102`, ESD
`7b4703576cf183abc09cf558148b5c8afb97d20c` (#418/PR420). Earlier four-tier/no-new-tier
wording is superseded by a485; retain accepted quantity/UI behavior. No whole-SHA
completion follows from this design or any one focused test slice.

## Owned execution evidence and handoff

All commands ran from this isolated workspace or its own asset directory; no
original repository refs, shared build outputs or persistent/provider data changed.
Local fixtures were uniquely named disposable Testcontainers with synthetic data.

- Private restore and final `dotnet build Legacy.Maliev.Web.slnx -c Release
  --no-restore` with the above owned workspace dependency properties: zero warnings,
  zero errors. The reviewed producer/auth preparation script built four private
  binaries, each with zero warnings/errors.
- Initial pricing focus: 160 passed. Expanded intended RED: 30 failed, 3 guard
  cases passed, 0 skipped. Actual-profile message had a separate intended RED.
- Final relevant commercial/profile/ticket/workflow/submission focus: 262 passed,
  0 failed, 0 skipped. Final Web suite: 2,516 passed, 0 failed, 0 skipped;
  `TestResults/commercial-final-full/commercial-final-full.trx`.
- Tier sensitivity proof: eight literal goldens passed; approved one-line
  wrong-tier control produced 7 assertion failures /1 passed; exact restoration
  rebuilt cleanly and passed all eight. Patch/TRX retained under
  `TestResults/tier-wrong-control-red`; no mutation remains in production.
- Full `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore`
  passed with `MalievWorkspaceRoot` and `UseLocalMalievDependencies` supplied as
  process environment properties. An initial invocation with unsupported `-p`
  arguments was rejected; this corrected complete command is the format evidence.
- Transitive NuGet vulnerability audit found no vulnerable packages. Existing
  `npm run ci` completed; final module tail: 565 passed, 0 failed, 10
  conditional skips. Committed browser assets remain byte-identical by Git diff.
- Historical manifest-only verifier passed its unchanged 159-entry checkpoint;
  it does not prove completion of the latest source cohort. `git diff --check`
  passed. Gitleaks of the exact tracked diff plus both new source/doc files found
  no secrets. Broad ignored-output scan reported two generated Playwright bundle
  generic-key matches; no scanner suppression or source secret was introduced.

Root independent acceptance: rebuilt all four pinned private producer/seed
projects and the Web Release solution with zero warnings/errors. Combined
calculator/commercial/minimum/pricing/ticket/submission focus passed 158/158;
complete suite passed 2,516/2,516, zero skips, in 8m47s
(`TestResults/root-commercial-full/root-commercial-full.trx`). Complete format,
transitive vulnerability audit, diff check and tracked-history secret scan passed.
The latter scanned 299 commits and found no leaks; staged candidate safety is
checked separately before commit. No public DTO, route, physical admission,
schema, persistent data, deployment or external-provider boundary changed.

This is the validated bounded #421 candidate, not an external release or completion
of parent #275. Root owns the protected PR, exact-SHA CI and individual-SHA
ledger dispositions. Those gates must pass before main integration is complete.
