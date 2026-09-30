# Resin server support evidence — Web #426

## Current reviewed implementation phase — 2026-10-01

The phase notes below preserve the earlier test-only and estimator-only gates;
they are chronology, not the current scope restriction. Root subsequently
approved the coherent server estimator, separately resolved provisional v2
composition, internal quote identity, protected ticket continuity and v11 resin
commercial consumer. Historical v1 and FDM behavior remain unchanged. The current
stacked parent is `b65464ebee175fbea35827072872b093e834aae6`; root separately
accepted its protected merged main `636ad950dc6a7d37e51a7d6bc4886dadd7ab20a9`.
Root independently reviewed the bounded implementation and owns validated
commit/push/protected-main integration. No whole-source ownership completion is
claimed. Independent Release built with zero warnings/errors; combined focused
tests passed 310/310 and the affected full suite passed 2,702/2,702 with zero
skips in 8m59s (`TestResults/root-resin-focus/root-resin-focus.trx` and
`TestResults/root-resin-full/root-resin-full.trx`). Whole-solution format and diff
checks passed after correcting an initial nonexistent `.sln` command to the
actual `.slnx`; that diagnostic is not passing validation. Frozen manifest-only
verification passed with 159 historical entries and explicitly did not inspect
the latest private source. It does not close the remaining global ledger.
Independent browser-module checks passed 176/176; the existing geometry engine
suite passed 565 with 10 existing opt-in private-corpus skips and no failures.
All four solution-project transitive vulnerability audits reported no known
vulnerabilities. Final staged secret scan examined 115,948 bytes with no leaks;
diff checks passed. A package-audit invocation initially restored default
adjacent dependencies; the exact private dependency graph was restored and
audited again before integration. No tracked dependency or asset files changed.

The defensive mixed-receipt issue/validation/unprotect tests observed four RED
before the minimal resin-only rejection. Their subsequent focused gate passed
303 cases. The pre-null-normalization full suite passed 2,695/2,695, zero skips
(`TestResults/426-full.trx`, 10m8s); this is diagnostic after later code changes.

Typed optional-profile tests first observed 3 RED/2 GREEN, then 4 RED/2 GREEN
after separately exercising absent demand with null area. Existing validator
accepts these null lists as absent. The approved repair normalizes only the
estimator's local area/demand lists, never the public DTO or FDM interpolator.
Absent demand still uses provisional area growth; explicit zero samples do not.
The fresh Release built with zero warnings/errors, but focused execution found
308 GREEN/1 RED: constant sampled area plus absent demand yields tiny positive
weighted-interpolation noise, which incorrectly enables the raft. An additional
non-null constant-area test independently reproduces this exact defect:
`426-constant-area-corrected-red.trx` has 2 RED/5 GREEN, including support noise
`4.2632564145606007E-19`. Its first helper fixture supplied empty area and passed;
that diagnostic is preserved, and the corrected fixture explicitly supplies 64
constant 500mm2 samples. Expected zero support/raft is not weakened. Root approved
estimator-local stable interpolation `a + (b - a) * t`, preserving validated
domain, clamp and absent fallback. Latest source has the same weighted rounding
defect: this is an explicit numerical-correctness deviation, not a claim of exact
source equivalence. No epsilon or FDM helper change is introduced. The fresh
Release after this repair built with zero warnings/errors. Final combined focus
passed 310/310, zero skips (`426-stable-final-focus.trx`). The owned test file now
has 136 cases: genuine arithmetic/consumer/ticket controls and existing
unavailable/compatibility guards, all synthetic rather than calibration proof.
Final full passed 2,702/2,702, zero failures/skips in 9m22s
(`TestResults/426-final-full.trx`). Final static gates passed. No old test
expectations were edited. Current scope includes actual `QuoteItem` resin monetary
routing, `InstantQuotationPricingService` selected/card paths and protected v4
profile composition continuity; the bottom exclusion list describes the initial
test-only phase only. Latest worker uncapped support/10% waste/browser money
remains excluded in favor of the explicitly provisional retained server capped
support/15% waste model. This is not manufacturing certification or whole-SHA
completion; parent #275 remains open.

### Final handoff gates — 2026-10-01

- `426-final-solution-build.log`: Release solution, 0 warnings/0 errors, after
  the sole post-full C# change (new-test object initializer whitespace only).
- `426-handoff-focus.trx`: 310 passed/0 failed/0 skipped after that whitespace
  correction; semantic final full remains `426-final-full.trx` (2,702 passed).
- `426-final-format.log` preserves the initial whitespace failure;
  `426-final-format-retry.log` is scoped nine-file `dotnet format --no-restore
  --verify-no-changes`, exit 0. `git diff --check` passes.
- `426-final-manifest.log`: `-ManifestOnly` frozen 159-entry semantic/sequence
  audit exact; private source comparison not run and no source writes. This
  historical manifest gate is not latest bed10 whole-SHA completion.
- `426-final-dotnet-vulnerabilities.log`: solution transitive vulnerability
  audit, zero vulnerable packages in all four projects.
- `426-final-npm-audit.json`: all dependencies, zero known vulnerabilities.
- `426-final-gitleaks-summary.json`: redacted direct-file scans of all ten
  changed tracked/untracked runtime/test/design files, exit 0, zero findings.
- Earlier `426-browser-assets-and-node.log`: `npm run ci` completed; 176 browser
  module tests passed, 565 engine tests passed and 10 existing optional private
  CNC corpus cases skipped. The private corpus is not mounted or distributed;
  no CNC implementation/acceptance claim. Committed browser assets unchanged.
- All eight private dependency repositories were read back at exact pins with
  clean status. Producer/auth disposable PostgreSQL/Redis fixture boundaries
  ran in the final full suite. No persistent database, provider or deployment
  writes were performed; container health evidence is disposable-test health,
  not deployed production health.

All commands/results are owned under this worktree's ignored `TestResults`.
No original source repository or old test expectations changed; no commit/push/GitHub write. Root
owns independent acceptance and integration; this lane releases its outputs
after final readback.

## Historical initial test/design-only authority — superseded 2026-10-01

Parent Web #275 remains open. This slice establishes server-side provisional
resin support/raft/waste estimation from validated sampled geometry. It does not
trust browser-reported resin totals, certify manufacturing geometry, activate a
material-specific production profile, or change monetary routing.

Owned worktree: `B:\maliev-legacy\.worktrees\web-resin-server-evidence-20261001`.
Branch: `codex/resin-server-evidence-20261001`.
Base: `c56d9cb9b4b2c351aab4893cda2ee89364ef3721`.

Ruling: root authorizes only this design and new
`Legacy.Maliev.Web.Tests/ResinServerSupportEvidenceParityTests.cs`, private pinned
dependencies, Release/focused baseline and literal RED. Skill defaults for runtime
implementation, extra ledger/workspace files, broad suites, commits and reviewer
dispatch do not expand this phase. Root performs the design/RED review. #423's
local `11fed` commit is not applied or assumed merged.

## Individual source mappings

Read-only source checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f`, isolated
mirror `B:\maliev-legacy\.artifacts\source-commit-mirror-20260930.git`.

| Full SHA | Bounded relation |
| --- | --- |
| `813f8ffdcd6b2cb1ed567f5264807133ddd482b7` | Occupied-plate cost allocation; already present, not reimplemented. |
| `cf9721daa399aeddf30e4714a9f62f6daee9daae` | Provisional generic evidence/cycle; existing target v1 provenance. |
| `a3c5c4a53907e03ac202620cc051393c20fc28e3` | Introduces server support estimator, support/raft evidence v2 and 15% waste after all resin components. |
| `2712f7d05d982ba7a3a2e4905c3ff61073d0f13a` | Later resin terminal-tier behavior; monetary consumer changes excluded from this estimator phase. |
| `70129d77be01fa7c2d1830cc00c1d6ab2240e1f2` | Retires geometry-only FDM, retains resin estimator unchanged. |
| `744d3c5225e7cbf7a88af5c1f4fdb47442e2c325` | Introduces browser resin calculation/client-total pricing; unsafe authority port excluded. |
| `ea0743c0c7e8653462eebe813af6c7a5dbfd8438` | Quantity floor/FDM support corrections, not a resin waste reconciliation; previously accepted calculator work remains separate. |

Committed ancestry checks against the checkpoint passed for a3, ea, 744, 813,
271 and 701. Source `PrintTimeCalculator.cs` a3-to-checkpoint diff removes FDM
code but does not alter the resin estimator. These are individual mappings,
not whole-SHA closure or completion of parent #275.

## Latest source divergence and actual consumer

At the checkpoint, `Maliev.Web/Pricing/PrintTimeCalculator.cs:15-153` calculates
model volume, footprint-capped overlapping support envelopes, conditional raft,
and **15% handling waste after model + support + raft**. Explicit sample demands
project to the base; absent demand uses area-growth with a 0.5 reach factor.
`ResinBuildProfileEvidence.cs` v2 supplies support density 0.15, raft ratio 1.10,
raft thickness 1mm and the full-layer exposure/lift/retract cycle.

The later worker `additive-simulation.worker.js:773-820` retains an uncapped
support sum and **10% waste**. Source page handler
`Pages/InstantQuotation/3D-Printing.cshtml.cs:836-842` accepts its client totals.
`ClientAdditiveSimulationPayload.ValidateResin` validates numeric bounds, not
matching profile composition or trustworthy geometric/manufacturing authority.
That source path is not adopted.

Target production flow is browser geometry claim -> admitted upload/coordinator
-> protected Put/Get -> authoritative pricing -> line/order tickets -> submission.
Its resin branch still calls `PricingEngine.QuoteItem`, which uses volume * 1.15
and ignores sampled support. Resin comparison cards share that branch. The
compatibility GET estimate route is also reachable, but issues no protected
submission authority. Neither flow consumes the retained worker's resin totals.

#423 preserves/hash-binds sampled inputs but does not make them server-reconstructed
manufacturing geometry. Its merge is a later consumer-integration prerequisite.
This phase tests only real estimator/evidence contracts; no session repair is
copied into this branch.

## Proposed estimator and composition contract

Use the retained server algorithm as a explicitly provisional calculation policy.
Keep model, support, raft and waste disjoint; apply waste once to their sum.
An explicit all-zero unsupported array means no support and no raft. An absent
array is unavailable evidence, not a fabricated zero array: preserve the source's
named conservative area-growth calculation and test it separately. Uniform area
when area samples are absent is a provisional analytical approximation, never an
admitted FDM or manufacturing fallback.

The source profile remains `research_provisional`, with generic material/machine
envelope and engineer review required. Support source, density, raft ratio and
thickness must be present, finite and valid before calculating. Unknown values
must not become zero or nominal guesses. Zero/empty estimator output denotes
unavailable; later price routing must explicitly refuse it rather than charging
only post-processing. That price-routing guard is not implemented in this phase.

Composition is not the existing vendor digest alone: a later protected consumer
must bind the exact cycle, support strategy/density, raft settings and waste
policy separately from commercial policy. Adding support fields while retaining
the old vendor digest does not certify a new material/profile composition. No
profile-ticket/version mutation is authorized now.

Bounded resource decision for review: maximum model-layer count 20,000. At the
existing 1000mm geometry limit with 0.05mm layers, a supported part adds 20 raft
layers, so final count is 20,020. Source browser validation's 20,000 *final-layer*
limit conflicts with this supported boundary; do not import it silently. Use
bounded layer arrays and interval/prefix accumulation to preserve capped envelope
semantics without the source's worst-case quadratic area-growth loop. Inputs
above the geometry/model-layer limits must be unavailable, not truncated.

The direct estimator's profile-length rejection follows the existing analytical
geometry contract: over 10,000 samples is invalid. Do not invent a 64/24-only
restriction for this compatibility model; those counts belong to the separate
protected browser claim admission contract. Source analytical fixtures also use
other normalized grids. Negative/nonfinite/out-of-range area or unsupported
samples invalidate the entire estimate rather than being dropped or clamped.
Null geometry and invalid height, volume or footprint are unavailable. A finite
0.0005mm layer cycle at 10mm has exactly 20,000 model layers; 0.00049999mm has
20,001 and must be refused, not silently capped.

Preserve current v1 `Assess` readiness and current pricing/catalog behavior until
a separately released catalog/routing slice. Missing four new nullable support
fields must not be invented for those existing profiles. The new partial-v2
composition RED and old-v1 compatibility GREEN require a separate estimator
composition gate (or explicitly scoped assessment), not a blanket new requirement
that breaks every existing v1 profile. Root must review this precise boundary
before runtime; profile/version/catalog migration is not implicit approval here.

## Historical inert compilation gate — superseded 2026-10-01

The target lacks `PrintTimeCalculator.EstimateResin`, `ResinEstimate` and four
support-evidence properties. Direct, typed estimator tests cannot compile today.
Reflection existence tests would not establish literal arithmetic RED; assertions
against `QuoteItem` would broaden this phase into monetary routing.

Root observed these missing types/members and approved only this inert scaffold:

- Internal source-named `EstimateResin(GeometryInput?, ResinBuildProfileEvidence?)`
  returning an all-default `ResinEstimate`, with no calculations or validation.
- Internal `ResinEstimate` with source-named `PrintMinutes`, `ModelResinMl`,
  `SupportResinMl`, `RaftResinMl`, `WasteResinMl`, `TotalResinMl`, all zero defaults.
- Nullable `SupportSourceUri`, `SupportEnvelopeDensity`, `RaftAreaRatio`,
  `RaftThicknessMm`, all null defaults on the internal evidence class.

No catalog initialization, validator changes, pricing, public DTO or route is
part of that approval. The scaffold is unimplemented and not commit-ready.
Compile typed literal tests and observe RED before any calculation is authorized.
Root additionally approved a real existing QuoteItem material-usage regression
to demonstrate the reachable consumer gap; it does not authorize price routing.

## Literal test-first acceptance

Synthetic arithmetic fixtures, not source-observed manufacturing calibration:
height 10mm, volume 5000mm3, footprint 500mm2, area profile 500mm2, 64/24 samples.
Complete synthetic evidence uses the reviewed v2 numeric composition but no
claim of operator material calibration. Expectations are literals, not production
helper-derived values.

| Unsupported fixture | Model ml | Support ml | Raft ml | Waste ml | Total ml | Minutes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Explicit zero samples | 5 | 0 | 0 | .75 | 5.75 | 48.02 |
| Last sample 120mm2 | 5 | .1791 | .55 | .859365 | 6.588465 | 52.52 |
| Samples 32 and 63 each 400mm2 (64 samples) | 5 | .67275 | .55 | .9334125 | 7.1561625 | 52.52 |

Derivation: 200 model layers; last demand maps to layer 199. Single support is
120 * 199 * .15 * .05 / 1000 = .1791ml. Overlap demand maps to layers 101 and 199;
capped envelope is 101 * 500 + 98 * 400 mm2-layers, not the worker's uncapped
.9ml sum. Raft is 500 * 1.10 * 1 / 1000. Normal cycle is 13.5 seconds, six
bottom layers add 30.2 seconds each; raft adds 20 normal layers.

Planned meaningful regressions:

1. Complete zero-demand and supported literals catch flat support allowance,
   missing raft, missing raft height, double material/waste and 10% waste.
2. Overlap literals catch absent per-layer footprint caps and support duplication.
3. 64/24 last-sample cases pin normalized-height mapping rather than fixed indices.
4. Missing unsupported samples with rising areas differs from explicit zeros;
   retain the named provisional area-growth semantics, no invented support evidence.
5. Missing/invalid source, density (null/0/negative/>1), ratio and thickness fail
   assessment and yield unavailable estimate. Valid synthetic composition must
   still calculate; workbook labels alone remain insufficient evidence.
6. Null/invalid profile and malformed geometry never yield an eligible estimate.
7. At 1000mm, volume 500000mm3, explicit zero demand: model500ml, waste75ml,
   total575ml, time4503.02min. Supported last120 demand: support17.9991ml,
   raft.55ml, waste77.782365ml, total596.331465ml, time4507.52min.
   Beyond maximum height/model-layer count is unavailable rather than truncated.
8. Dense rising-area absent-profile fixture at maximum height exercises the whole
   layer budget: support18.75ml, raft.55ml, waste40.395ml, total309.695ml,
   time4507.52min. It does not rely on a machine-dependent stopwatch assertion or
   by itself prove algorithmic complexity. Runtime review must verify bounded work.
9. Existing real QuoteItem with supported last120 samples must consume6.588465ml
   rather than5.75ml. This test observes the actual consumer gap independently of
   the inert estimator shape; monetary routing is still gated separately.

Future runtime ownership requires a separate root gate: estimator/models/evidence
validator and only the necessary resin constants. Monetary routing, commercial
policy changes and profile-ticket binding require their own RED before release.

## Execution ledger

### Mixed-receipt defensive review and final validation gate

Root confirmed protected main636ad950dc6a7d37e51a7d6bc4886dadd7ab20a9 exact CI
36771208716 SUCCESS, then reviewed the runtime and requested the mixed receipt
guard. New typed tests first observed3 RED, then4 RED when actual Validate was
included (`426-mixed-receipt-red.trx`, `426-mixed-receipt-expanded-red.trx`):
resin+stale FDM receipt issued/validated and resin tickets accepted nonempty FDM
analysis/hash fields. Minimal Issue/Validate reject nonnull PhysicalReceipt on
resin; Unprotect rejects nonempty physical fields. FDM checks remain unchanged.

Full preparation used the inspected repository helper in private dependencies;
producer/auth/seed fixture connections must be GUID-named high-port loopback
Testcontainers, never persistent databases. No live/provider data was touched.
Fresh solution Release0W0E. Browser-module176/176 passed; Node engine tail was
still active when this solution build started, an execution-order overlap with
independent owned outputs (not claimed fully serialized). Hold remaining .NET
test/build/static gates until Node terminal, then run sequentially and preserve
all diagnostics. Final acceptance results will be recorded when terminal.
Node CI is terminal: browser-module176 passed/0 skipped; engine565 passed,
10 optional private-CNC-corpus skipped,0 failed (575 total). The private corpus
is not mounted/distributed; no CNC implementation or acceptance is claimed.
Tracked browser assets unchanged. All four pinned producer/auth/seed builds were
repeated sequentially and verified0W0E (`426-boundary-four-builds.log`).
Fresh test-project Release0W0E and defensive combined focus303/303 passed,
zero skipped (`426-defensive-final-focus.trx`), including129 owned cases plus
existing compatibility/tickets/submission. Full affected suite is running with
all four required owned DLL environment variables (`426-full.trx`, `426-full.log`);
no full outcome claim until terminal. Remaining static checks are queued after it.

### Coherent consumer runtime gate

Root read/released the exact consumer/composition plan and accepted admission
maximum20160minutes. Before hash repair, four missing Class/Source provenance
cases and two equivalent decimal-scale/UTC-offset cases built0W0E and all6 failed
(`426-extra-hash-red.trx`). Historical Assess remained Ready in the missing Class/
Source tests, proving the digest cannot rely on its weaker v1 evidence criteria.

Runtime now resolves a separate exact source-v2 provisional object (v1 untouched),
hashes33 tagged UTF8 byte-length fields plus fixed capped15%/half-reach algorithm
tag, and normalizes decimalG29 and approval UTC O. Null/incomplete fields reject;
the same composition criteria serve estimator/assessment. QuoteItem uses actual
estimate before costing, bounds finite time/usage and decimal-admissible direct
cost for maximum quantity/margin headroom, then existing decimal commercial
subtotal and ceil10 unit×quantity. Only resin's terminal display tail collapses;
FDM branch economics are retained. Selected unavailable throws, cards catch that
and remain null. Exact internal identity propagates ItemQuote→PartQuote→existing
v4 ticket fields, with current-composition checks on Issue/Unprotect/Validate.
Commercial revisionv11 is separate; no FDM receipt, public DTO, schema, TTL,
session, worker or manufacturing-authority change.

First runtime freshRelease0W0E;123 focused cases121 passed/2 failed/0 skipped
(`426-consumer-runtime-focus.trx`); unchanged58 compatibility passed
(`426-consumer-runtime-compatibility.trx`). Failures reveal too-aggressive new
terminal-display literals, not algorithm divergence: independently calculated
q500 cost196.945169398333 givescommercial121112.30/unit250; q1000 same direct cost
givescommercial239210.28/unit240; q5000/10000 are240. Source tail-collapse therefore
retains1000, not500 as the first repeated240 anchor. Exact two array/design
correction is held for root review; runtime is not changed to collapse distinct
250→240 prices. Root independently verified this exact decimal sequence and
approved only the two new arrays plus q500/q1000 literal money cases. Original
incorrect-array failures remain preserved. No old test expectation was edited
and no full suite run yet.

Fresh corrected runtime Release0W0E; new125/125 GREEN/0 skipped
(`426-consumer-runtime-final-focus.trx`), unchanged58/58 compatibility GREEN
(`426-consumer-runtime-final-compatibility.trx`), existing ticket/submission116/116
GREEN/0 skipped (`426-consumer-runtime-ticket-submission.trx`). No old test or
expectation was changed. Root receives this minimal runtime diff/focus before
authorizing full verification; no full-ready or accepted-main claim.
Post-correction scoped9-C#-file format verification exited0; diff whitespace check
passed. Owned dependency pins rechecked unchanged. No active outputs remain.

### Stacked v4 parent and inert consumer compilation gate

Root explicitly replaced the wait-only prerequisite with a stacked test/design
gate. Non-destructive `git merge --ff-only b65464ebee175fbea35827072872b093e834aae6`
succeeded and preserved all owned dirty tracked/new files. This is the reviewed
PR427 parent, now protected-merged as636ad950dc6a7d37e51a7d6bc4886dadd7ab20a9;
its post-main36771208716 was pending at authorization. No exact-main acceptance
claim is derived from testing this stacked branch. Root serializes final integration.

Fresh parent Release0W0E and original70 focus retained59 GREEN/11 consumer RED
(`426-v4-parent-70-red.trx`). Only approved inert compile shapes were then added:
`ResolveProvisionalSupportProfile` still returns originalv1, `CreateSha256` still
returns empty, and internal `ResinQuoteProfileIdentity` metadata remains null on
ItemQuote and PartQuote. No record positional constructor/public accessor expanded;
JSON does not include internal metadata even when a test populates it. These shapes
are unimplemented/not commit-ready, not catalog or hashing acceptance.

New typed tests inspect committed-v2 provenance through the separate resolver,
mutate each of33 resolved composition fields, distinguish field-boundary collisions,
check culture-independent64hex identity distinct from vendor hash, reject null/v1/
partial hashing input, require actual priced internal metadata and prove JSON
omission. Direct real ticket tests independently exercise old-v4/v10 line AND order
rejection and current-v4/v11 line AND order acceptance, plus geometry-demand mutation
rejection under the actual v4 parent. The parent geometry continuity guard passes;
new policy/catalog/digest/population cases are intended RED before implementation.

Canonical digest proposal for runtime review: domain `maliev.resin-composition.v1`,
fixed ordinal property tags, UTF8 byte-length-prefixed values (including source/
approval evidence), decimal invariant normalized representation, UTC invariant
timestamp, explicit server capped15% waste-policy tag. Reject missing evidence;
never coerce null to zero/empty or concatenate without field boundaries. Changes
to fixed waste/algorithm identity change that tag. Commercialv11 is separate.

The initial new build failed on C#14 contextual `field` in MemberData's property
lambda; renamed to `name`. Accidentally executed no-build checks after that failed
build consumed the old70-case assembly (`426-composition-policy-red.trx` and
`426-composition-v4-compatibility.trx`); these are NOT new-stage validation evidence.
Fresh corrected Release0W0E preceded the real116-case run:55 RED/61 GREEN/0 skipped
(`426-composition-policy-final-red.trx`). Existing resin/pricing+parent geometry
continuity58/58 passed (`426-composition-v4-final-compatibility.trx`). A final
field-boundary collision case was then added and will be rebuilt before its result.
Final fresh Release0W0E; terminal117-case focus56 RED/61 GREEN/0 skipped
(`426-composition-policy-terminal-red.trx`). Composition40 failures, policy5 and
existing consumer11 are intended, distinct missing behavior. Parent58-test
compatibility focus passed again, zero skips (`426-composition-v4-terminal-compatibility.trx`).
JSON omission and actual v4 geometry mutation rejection are independently GREEN;
all59 estimator/guards remain GREEN. No new runtime calculation/hash/catalog/
nominal-money/policy/ticket behavior was implemented in this compile-shape phase.
Scoped five-C#-file format verification exited0; `git diff --check` passed.
Six owned changed files remain (four tracked production files including approved
inert shapes, new test and design); no active outputs or external writes/commits.

## Historical consumer integration design — subsequently approved 2026-10-01

There is no `QuoteBatch` symbol in the target C# or latest committed source
`Maliev.Web`. The actual batch consumer is
`InstantQuotationPricingService.QuoteCore`: selected resin uses `QuoteItem` at
line186; resin comparison cards independently use it atline223. These are the
production callers to repair, not a new invented batch interface. Compatibility
GET preview shares QuoteItem; it must not mint manufacturing authority.

Prerequisites: root integrates #423 geometry/session continuity (line schema and
purpose v4) and #424 through exact-main green, then serializes rebase and fresh
Release/focus. This c56 candidate still has line-v3; tests use real Issue/Unprotect
without porting #423 or pretending v4 is already here. After integration, those
same tests must prove v4 money/profile roundtrip, geometry mutation rejection,
expired authorization and old-line/order replay rejection using the existing
protected continuity/submission suites. Unchanged source worker remains10%waste
and uncapped; source server retained15% capped is the explicitly provisional
authority choice. Do not pass browser resin totals into protected price routing.

Coordinated migration plan for root review:

1. Keep historical v1 evidence and its all-four-absent Assess behavior intact.
   Publish a separately named complete provisional v2 composition when routing
   changes, with exact source provenance from a3: cycle source Phrozen Mighty4K,
   support source `https://helpcenter.phrozen3d.com/hc/en-us/articles/6371322306073-Suggested-support-settings`,
   support strategy v1, density.15, raft1.10/1mm, solid-by-default policy and waste.15.
   Do not turn this generic research envelope into certified M68/CASTWAX profiles.
   New consumer catalog tests require their own RED before that population change;
   preserve v1 as an explicit historical fixture, not nominally populate its nulls.
2. The source vendor hash stays a provenance hash, not the exact composition hash:
   a3 v2 retained `4CA2897D2D2A1DF72C17E959848979D33BAA74EB3310EF523F981F6950BCBBC7`.
   Canonically hash tagged/length-delimited invariant values for version, machine/
   material envelope, cycle settings, support source/strategy/density, raft values,
   hollow policy, plate settings, handling evidence and the server waste-policy
   identifier. Reject missing required values. No commercial margin/tax policy in
   that physical composition digest. One-field mutation tests must each change it;
   equivalent invariant serialization under cultures must preserve it.
3. QuoteItem resolves complete v2 once, estimates, and refuses unavailable/nonfinite/
   nonpositive/out-of-commercial-admission outputs before ResinDirectCost. This
   prevents charging only wash/consumables for failed physics. Do not fallback to
   v1 or browser geometry money. Carry the exact resolved version/digest internally
   from that price into selected line/ticket rather than label it with commercial
   policy or recompute a potentially different profile after pricing. Prefer
   internal-only metadata on existing quote models, excluded from wire serialization;
   no routes/DTO expansion, no fake FDM physical receipt. Comparison cards use same
   resolver; unavailable candidates remain null/manual, selected unavailable fails
   closed through existing coordinator error behavior. A huge finite cycle estimator
   guard is arithmetic proof only: consumer must bound plausible time (reviewed
   source client limit20160 minutes is an anchor, not trusted browser authority)
   and all decimal conversions before charging. The approved generic cycle fits.
4. Use existing `CalculateStandalonePrice`/decimal commercial calculator for resin,
   including setup, packaging, occupied plates/reserve/payment gross-up; customer
   unit is ceil-to10 of CommercialSubtotal/quantity, then multiply quantity. Do not
   import calculator gross into public order: sum rounded line subtotals, existing
   minimum surcharge/delivery/VAT and conservation allocation remain untouched.
   Collapse only repeated terminal display prices, preserving the active quantity.
5. Genuine changed commercial behavior requires next commercial policy revision
   (proposed v11), distinct from v2 provisional composition and v4 line schema.
   Update ticket Issue/Validate to bind actual resin composition through existing
   ProfileVersion/ProfileSha256 fields, not PolicyVersion fallback. Old-policy
   tickets cannot silently authorize new economics: reject and requote, retaining
   uploads/protected session and immutable historical amounts. Old-v4/v10 order
   replay under new policy must fail, even with unchanged geometry and valid TTL;
   v4 purpose, order2/upload1/session1 TTL/CAS stay unchanged. This intentional
   short-lived authorization invalidation is not historical order repricing.

Proposed narrow future production ownership (requires new gate): PricingEngine
resin branch/terminal tier helper, evidence catalog/composition identity, internal
quote metadata, PricingCatalog commercial revision only, pricing service selected/
comparison handling, ticket Issue/Validate resin identity. No FDM estimator/worker,
upload provenance, CNC, public routes, contracts or serializer shape expansion.

### Independent commercial literals and typed consumer RED

New Consumer_* tests call actual QuoteItem, actual multi-part pricing service and
actual EphemeralDataProtection Issue/Unprotect, not mock prices or production helper
expected values. Synthetic support fixture:6.588465ml,52.52minutes, footprint500,
capacity30; M68 cost2.14/ml. Shared plate cost =52.52*(29/60+93329.1/43200);
per-part material/handling =6.588465*2.14+178.125. Occupied plates=ceil(q/30).
Setup54.6875, packaging30, reserve.15, fee.03, VAT.07, process floor500. Commercial
subtotal = rounded gross minus unrounded VAT and explicit minimum surcharge.

| q | direct/unit THB (independent arithmetic) | commercial subtotal THB | rounded unit | public subtotal |
| ---: | ---: | ---: | ---: | ---: |
| 1 |331.072970933333 |878.70 |880 |880 |
| 10 |206.109180683333 |3663.67 |370 |3700 |
| 30 |196.852603627778 |10327.27 |350 |10500 |
| 31 |201.182292895699 |10897.44 |360 |11160 |
| 500 |196.945169398333 |121112.30 |250 |125000 |
| 1000 |196.945169398333 |239210.28 |240 |240000 |
| 10000 |196.861860204833 |2334018.72 |240 |2400000 |

The31 case is the partially occupied second plate and catches full-plate-only
division. Two actual lines(q1+q10) must produce items4580, delivery100, VAT327.60,
grand5007.60, allocated962.16/4045.44. M68 cards equal selected880/370. Terminal
display quantities collapse to1/10/50/100/500/1000 (append10000 if active). These
expectations detect estimator omission, old AllIn pricing, waste twice/10%, wrong
quantity tier, wrong plate count, gross/public money mixing and premature rounding.
Profile ticket identity must be v2 plus a64hex composition digest distinct from
vendor hash and commercial policy. Catalog/digest mutation/replay tests that need
new interfaces remain design requirements, not fabricated existing runtime proof.

This design-only expansion changed only the owned test/document, not runtime.
Initial build caught xUnit2031 in the new test (filtered Assert.Single); corrected
to the predicate overload, then fresh Release0W0E. Terminal new-file suite:
59 passed /11 failed /0 skipped /70 total (`426-consumer-design-red.trx`). All59
estimator/guard cases pass, including final raft20000-layer edge, next-layer/huge
raft refusal and huge finite cycle double arithmetic; consumer11 remain intentional
RED. Existing compatibility35/35 passed (`426-consumer-design-compatibility.trx`).
Actual failures include order items4410 vs4580, protected resin profile labelled
commercialv10, uncollapsed terminal tiers, wrong literal unit prices, missing
unavailable-estimate refusal and old5.75ml support usage. No mutation-control runtime
change performed in this phase; literal input/output tests are mutation-sensitive
but not a claimed executed mutation audit. Protectedv4/newpolicy replay and digest
mutation tests are deferred until root permits the required integration/interfaces.
New-test scoped format verification exited0 and `git diff --check` passed; no
active test/build/format output remains. No full-suite or complete #426 claim.

### Historical estimator-only runtime gate — subsequently integrated 2026-10-01

Root subsequently approved runtime in the three existing scaffold files only.
`Assess` requires the shared support-composition helper if any of the four fields
is present; all-four-absent legacy v1 assessment remains unchanged. The estimator
always requires that helper independently. No catalog is populated and no pricing
caller is enabled. Invalid geometry is refused before interpolation; malformed
samples cannot be abs/clamped/truncated into an eligible result.

Demand samples are aggregated per normalized layer, then projected as interval
deltas. One prefix scan caps active support at the footprint, retaining the source
envelope semantics in O(samples + modelLayers), without nested layer expansion.
Model layers are bounded to20,000; additional raft layers are bounded to20,000
(combined at most40,000). No array scales with raft layers. A supported canonical
1000mm/.05mm fixture still has20,020 final layers. All counts are checked as finite
doubles before integer conversion; derived total/time must be finite and positive.
Cycle arithmetic uses double conversion before division, avoiding decimal division
overflow for otherwise finite evidence. These numeric bounds are safety guards,
not material/profile calibration or protected production admission.

The first runtime estimator run was53 passed/2 failed: both last120 goldens had
mistakenly doubled support (.3582 instead of.1791). Root independently recomputed
120*199*.15*.05/1000 and read the latest source algorithm, approving the exact
literal/design correction only. `426-estimator-green.trx` preserves those failures;
runtime was not changed to double support. Correct waste is.859365 and total6.588465.
The real QuoteItem future expectation changed to that corrected literal; consumer
implementation remains deliberately excluded and its test remains RED.

Final runtime verification: fresh Release0 warnings/0 errors; estimator-only focus
55 passed/0 failed/0 skipped (`426-estimator-final-green.trx`); unchanged legacy
resin/pricing focus35 passed/0 failed/0 skipped (`426-runtime-final-v1-compatibility.trx`).
Real QuoteItem consumer1 failed as required: expected6.588465, actual5.75
(`426-consumer-held-red.trx`). No full-suite/complete-feature claim: remaining
consumer RED is intentional and must pass after a separately approved integration.
Post-runtime scoped format verification exited0; `git diff --check` passed.
At this historical estimator-only gate, scope search confirmed no production
caller and no active outputs. The current approved consumer now calls it through
`PricingEngine.QuoteItem`; see the current summary above.

- Preflight clean linked worktree on the named branch/base; no superproject.
- Private local clones with independent Git objects/no shared outputs:
  Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`;
  Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; both clean detached.
- No `global.json`; repository targets .NET10 with warnings as errors.
- Initial Release build: 0 warnings, 0 errors.
- Focused existing resin-evidence/pricing baseline: 35 passed, 0 failed, 0 skipped;
  `TestResults/426-baseline.trx`.
- Root approved the internal default-only shape after inspecting the missing API.
- First scaffold/new-test Release: 0 warnings, 0 errors. Initial RED: 22 failed,
  4 passed, 0 skipped (`TestResults/426-red.trx`). Nine support assessment gaps,
  twelve estimator arithmetic gaps and one real QuoteItem usage gap were observed.
- Expanded malformed-input/negative-raft/budget Release: 0 warnings, 0 errors.
  First expanded RED: 25 failed, 30 passed, 0 skipped
  (`TestResults/426-expanded-red.trx`). Passing unavailable guards exercise the
  current validator and default-only scaffold; they are not implemented-estimator
  proof. Positive literal controls remain RED. Budget guard was then split into
  its own test so an earlier positive assertion cannot prevent its execution.
- Final expanded Release: 0 warnings, 0 errors. Terminal RED: 25 failed,
  31 passed, 0 skipped, 56 total (`TestResults/426-final-red.trx`): eleven partial
  composition failures, thirteen positive estimator literal failures and one real
  QuoteItem usage failure. Separate finite next-layer guard actually executed.
- Unchanged existing resin/pricing baseline rerun: 35 passed, 0 failed, 0 skipped
  (`TestResults/426-compatibility-baseline.trx`). Current Ready v1 retained.
- Scoped format verification passed before expansion and after final expansion
  (exit 0, four owned C# files). `git diff --check` passed; scope readback confirms
  only the three approved inert scaffold files plus the new owned test/design.
  No calculations, catalog/validator/pricer/ticket changes or full-suite claim.
  Runtime implementation remains gated on root review.

Build command uses absolute owned workspace root:

```powershell
dotnet build Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-resin-server-evidence-20261001/.dependencies -p:UseLocalMalievDependencies=true -v:minimal
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ResinBuildProfileEvidenceTests|FullyQualifiedName~InstantQuotationPricingTests" --logger "trx;LogFileName=426-baseline.trx" --results-directory TestResults -v:minimal
```

Initial test-only phase exclusions (superseded for approved resin monetary routing
and internal protected profile continuity): worker/client-money trust, monetary routing, profile tickets, public
contracts, UI, automatic material grants, provider data, config, persistent data,
deployment, GitHub writes, commit/push, CNC/STEP and other owned worktrees.
