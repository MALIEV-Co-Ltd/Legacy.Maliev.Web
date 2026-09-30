# Web397 tests-first safe-preview gate

## Ownership and immutable evidence

Owned worktree: `web-wall-thickness-parity-20260930`, branch
`codex/wall-thickness-parity-20260930`, base
`4483e3eb768e8ee59a4a2cf6ba9e2f452e0f3ed0`. Root approved the bounded
preview-only runtime lane after tests-first RED. Ownership includes only the
new parity test/fixture, focused JS tests, this design, workflow interop/component,
one Thai resource entry and its deterministic generated workflow bundle.
Existing large tests, pricing writer, coordinator, ticket, CI and source repository
are unchanged. No commit, push, deployment or persistent data write.

Committed original objects are read from the source mirror at checkpoint
`bed10c7d15e0698e0b75f1329d0f312937f5d77f`, never from dirty original files.

- `fad52019c898ebe63c39eb0f30e65b0fb59126ca`: four tests: thin FDM quote,
  retained evidence on resin change, reliable one-millimeter slab, open sheet.
- `483a2fdb0beeafa014bb6b0a5ca1e85cd7bdbb74`: adds the fifth coarse opposing
  face scenario; `351825232f787ef988c7c058910520a14aa70123` refines clipping
  and gradients. Thin-only overlay predecessor:
  `3f766fa00f23fffda6c15cab2ba231117c6eee50`.
- Historical design/plan: `774e0c19c7a6e6f4e8009e0b69be2a71957d2204` and
  `07e92563f661354db12460f1687f5e03be70aa5b` are Workflows-only resolutions,
  not application acceptance.

Current Workflows checkpoint is `cc7c7a99a8945163fd2ed023352add7e933806d5`.
Historical checkpoint `aeff71cde6c42b1e1c523ab21c0759902cbb4b15` records fad52019
pending with old issue links344/281. The three overlay sources retain their
PR292 resolution at `94931cea28e1d1c95b9a788a2d2d94afdab2a8e0`; do not
replace that historical provenance with this new test slice. Final acceptance
must append #397 evidence and resolve fad52019 individually, not globally.

## Flow, classification and observed gap

The flow under test is: `/instantquotation/3d-printing` -> actual selected STL
and genuine browser worker/viewer -> retained material-relative advisory or
honest incomplete preview, without granting physical pricing authority.

Browser plugin not available. The repository already pins Playwright1.61.0;
use its Chromium/Kestrel component harness, with no new browser dependencies.
The fixture uses only the additive URL and verifies the served thickness worker
bytes against the owned worktree. Browser component correlations are not
server-admitted identities, uploads or tickets.

Existing tests demonstrate localized thin-wall warning, price-neutral toggling,
review eligibility and one-millimeter non-warning. New component guards load
real STL bytes through the production loader and geometry analysis, use the
production WebGL viewer and genuine thickness worker, then inspect resulting
evidence/material state. No fake measurements or positive volume are supplied.
Their role is to establish coverage, not to invent a runtime defect if they pass.

The real-file-picker RED requires the English/Thai incomplete advisory and a
visible preview for a zero-volume sheet, with zero price tiers/DFM-clear claims.
Current admission requires positive X/Y/Z, bounding volume and actual volume;
the sheet fails before a quotation part exists. Reconciliation quarantines its
preview; normal thickness reporting rejects absent parts. The existing generic
upload error is therefore not sufficient source parity. Issue397 comments
explicitly preserve fail-closed pricing; no source quote fallback is authorized.

## Approved minimal runtime lane

Keep server claim validation, upload admission, authoritative mesh normalization,
physical receipt creation, pricing and tickets unchanged. Add a separate,
browser-local preview-only correlation for a narrowly classified planar STL:
successful bounded parsing, finite nonnegative dimensions with exactly one zero
dimension, positive surface area/facets, non-watertight topology and zero volume.
Any unsupported, failed, over-budget, nonfinite or ambiguous result retains the
existing generic error/quarantine behavior. It must never normalize volume to a
positive constant or send this preview through `admit` as a quotation part.

Classification belongs to the preview boundary, not the commercial state.
Expose only a small advisory snapshot (preview key, classification, worker
incomplete state/revision), never raw triangles in .NET, FileService references,
claim overrides or a protected receipt. Use at most the existing admitted-mesh
input/mesh limits (8 MiB selected bytes/50,000 triangles) for this exceptional
preview path, rejecting before a thickness worker or exceptional viewer allocation
beyond those limits. These are deterministic input/triangle limits, not a claim
that browser process/GPU overhead is capped at 8 MiB. Selected bytes are cleared
when retaining geometry. Parsing remains within the existing file picker budget;
rejected zero-volume planar candidates never enter the larger 400,000-triangle,
45-second normal-part thickness analyzer. One active
exceptional preview bounds retained memory; multiple selections, replacement,
remove/cancel and component disposal must release it and invalidate its reports.

Render a distinct `data-workflow-incomplete-preview` region with an actual
canvas, localized incomplete/estimated warning and explanation that an instant
physical quote is unavailable. Keep normal quotation parts/configuration
separate. No success/DFM-clear indicator, price tier, preliminary document,
checkout or submission becomes available for this preview. A safe path means
repair/re-upload/remove guidance, not an unpriced part inserted into protected
review. If the owner requires a manual-request CTA, inspect/approve its actual
submission contract separately before adding it.

Use separate preview-key + revision guards. Do not loosen
`ReportThicknessStateAsync`'s existing-part check. Late worker reports after
replacement/cancellation cannot resurrect previews or update admitted parts.
Unavailable coverage never yields a minimum thickness or safe/green display.
The generic upload error may remain present but must not hide the incomplete
preview or imply a quote exists.

Proposed runtime files, only after root gate:

1. `wwwroot/src/app/js/instant-quotation/workflow-interop.mjs`: preview-only
   classification/correlation and lifetime API, separate from `admit`.
2. `Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor.cs`:
   distinct local preview state/report guards and selection/error reconciliation.
3. `Components/Pages/InstantQuotation/InstantQuotationWorkflow.razor`:
   localized incomplete preview region and safe remove/re-upload affordances.
4. `Resources/Components/Pages/InstantQuotation/ThreeDimensionalPrintingEstimateContent.th.resx`:
   translations only if existing English-key/Thai strings are insufficient.
5. Existing deterministic `wwwroot/dist` assets produced by the normal asset
   build, plus focused JS tests for the new local lifetime API.

No calculator/catalog, coordinator, application/infrastructure contracts,
physical simulator, server ticket or CI change is proposed. #275/#309 do not
block an independent advisory component lane, but their protected-price rules
remain prerequisites whenever quote success is asserted. Existing browser
tests may need approved additive assertions later; do not edit them at this gate.

## Verification and stop condition

Private clones are exact CI pins under ignored TestResults/.dependencies:
Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, Contracts
`78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. No shared outputs or node tree.

Baseline Release build passed with zero warnings/errors; existing focused
wall-thickness/ordering suite passed12/12 in91 seconds. Root's exact-tree full
2481 baseline is not redundantly rerun for this tests/design gate.

Final tests-only Release build: zero warnings/errors in8.90 seconds. New five
cases: three passed, two failed, zero skipped in12 seconds. The passing cases
prove retained genuine worker evidence/reliability and the production WebGL
coarse projection. Both failures are actual missing English/Thai incomplete
advisories after a real sheet upload, not a fixture error or selector timeout.
They first confirm no price tier, no DFM-clear declaration, no page exception
and no framework error overlay. The screenshot displays only the generic
upload error; there is no retained preview.

Artifacts: `TestResults/wall397-baseline/wall-baseline.trx` and
`TestResults/wall397-red-final/wall-parity-red-final.trx`; English/Thai screenshots
are under `TestResults/wall397-red-final/screenshots`. Early setup mistakes
(CNC-only identity metadata on the additive route and a missing viewer snapshot
part argument) were repaired in the fixture/tests, with no runtime edits.
Scoped whitespace verification and Git whitespace checks passed. The full
three-file candidate passed the redacted Gitleaks scan. Both exact dependency
clones remain clean. No asset change/build is needed to observe this RED;
asset rebuild verification remains mandatory for any later runtime edit.

Before runtime, root reviews the terminal RED, passing genuine-component
guards and this design. A later implementation must add cancellation, stale
report, replacement, malicious/nonfinite/over-budget and normal admitted-part
nonregression cases; English/Thai desktop/mobile rendered interactions; actual
asset build/module suites; Release0W0E, focused and affected/full suites; scoped
format/security/static checks; protected PR and exact-main CI. Component browser
proof is not full Aspire, production TLS, hosted delivery or deployment proof.

## Implementation and follow-up review evidence

Separate key/revision state never enters normal admitted parts or server pricing.
Every accepted new selection batch replaces only the exceptional preview, including
normal or malformed replacements; existing admitted parts remain untouched.
Quarantine/release clears matching .NET state before releasing JS resources, and
late reports require the current key/revision. Removal/disposal invalidates the key.
Only exceptional planar preview materials become double-sided: neither geometry,
normals, dimensions nor any claim field is changed, and no material clone is added.

Safety RED before initial runtime: new JS16 failed; four localized desktop/mobile
browser cases failed for missing advisory/region. Quarantine added an observed
key-retention RED. Initial repaired full checkpoint:2488 passed/0failed/0skipped,
9m44s (`TestResults/wall397-full/wall397-full.trx`). This is historical evidence,
not final validation of later lifecycle fixes.

Root lifecycle review added actual rendered replacement/cancel/quarantine/release
and late-callback regressions; real framebuffer checks prove foreground, not just
canvas visibility. Follow-up RED:13 C# cases,4passed/9failed (four blank framebuffers,
three stale rendered lifecycle regions, two retained replacement previews),37s,
`TestResults/wall397-lifecycle-red/lifecycle-red.trx`. JS19 cases:17passed/2failed,
both actual analyzer worker-factory counts1 instead of0 for rejected planar budgets.
After minimal repair:13/13 C# GREEN26s and176/176 browser-module cases GREEN,
including19 focused preview cases. Four foreground screenshots are under
`TestResults/wall397-lifecycle-green/screenshots`. Actual worker evidence and normal
material color/vertex-color/side are preserved. These are intermediate checkpoints;
final evidence follows below.

Release candidate build after lifecycle fixes:0warnings/0errors8.14s. Commands use
`dotnet build Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release`
with `UseLocalMalievDependencies=true` and absolute `MalievWorkspaceRoot` pointing
to this worktree's `TestResults/.dependencies`. All displayed Release output paths
are owned. A mistaken earlier command used unsupported override aliases and wrote
sibling Defaults/Contracts Debug outputs; that build is discarded, sibling source
statuses were clean, no shared output was deleted/repaired, and root was notified.
Only the corrected private graph is acceptance evidence.

The ordinary asset build is deterministic: workflow bundle SHA256
`DAA942ABC66C91844E1468F52EF8C0E1A80A7E51CFD565007CEF544BAAE2181C`.
No other generated asset changes. Root extended ownership to the existing
`package.json` browser-module command only: the new test file is explicitly listed
alongside157 existing cases (176 total). No lock/dependency or CI configuration
change. No CNC code is changed.

## Pre-rebase acceptance checkpoint (2026-10-01, Bangkok)

Root async review identified an attachment-response race independent of worker
reports. Actual component RED:5/5 failed for delayed replacement/remove/disposal,
lost eligibility, and a stale failure clearing a new key. The repair rechecks key
and disposal after both retain and fresh eligibility awaits; a stale failure never
removes a new key. Subsequent root review removed a forced test rerender from
current failure/ineligibility cases:8 controlled cases produced6passed/2failed for
real stale rendered regions. Second-eligibility-await replacement/disposal controls
passed. Only still-current cleanup now requests a render when not disposed. No
general remover/render loop or ordinary viewer path changed.

Final new C# cases:21 total, comprising9 actual Chromium worker/viewer/file-picker
cases and12 actual rendered-component lifecycle cases with controlled JS timing.
The latter seed only local exceptional state, never admitted physical state/auth.
Pre-rebase private Release build:0warnings/0errors11.23s. Combined focus:33/33passed,
0failed/0skipped,1m40s (`TestResults/wall397-final-focus/final-focus.trx`), including
all12 existing wall-thickness/ordering cases. Pre-rebase full affected suite:2502/2502
passed,0failed/0skipped,10m44s
(`TestResults/wall397-final-full/final-full.trx`). Earlier full2488/2494/2499 results
are retained checkpoints, not substituted for this final run.

Terminal ordinary `npm run ci` includes the new required test entry:176/176 browser
modules passed, including19 preview cases. Its geometry suite passed565 with10
existing skips,0failures,575total,329.2s. Root explicitly approved reuse after the
last C#-only render repair, conditional on unchanged inputs. SHA256 readback proves
the same package, JS implementation, JS tests and generated asset bytes:

- `package.json`: `EAEAEAE2C2BB2A192D30F670ADE5474B0F783157747D5E3F8B93B1E6EB369941`
- `workflow-interop.mjs`: `44AF6642C2B6040CA9779C1DC3A216C6FEA76AAC8A1699F2C3093F3C2A99198A`
- `instant-quotation-incomplete-preview.test.mjs`: `570A72A24AA12A46B2CC7C040BBD05EA8CC253C39B6F6506B0C3ED06667C3FAF`
- generated bundle: `DAA942ABC66C91844E1468F52EF8C0E1A80A7E51CFD565007CEF544BAAE2181C`

Final English/Thai1280/375 screenshots are under `TestResults/wall397-final/screenshots`.
Each real file-picker case proves non-background WebGL foreground pixels, visible
mesh, genuine worker-complete localized advisory, no price/DFM-clear declaration,
no page/framework error, no horizontal overflow, and keyboard removal. Exact
localized completion waits replace an observed one-case worker/render timing race;
no sleeps, weakened assertions or existing-large-test edits were used.

Git whitespace, scoped .NET whitespace verification, JS syntax, JSON parsing,
resource XML/unique keys and redacted scoped Gitleaks passed. .NET transitive and
npm including-development dependency vulnerability checks found0. All private pins
remain clean. Tests use only disposable local hosts/data; this is not full Aspire,
deployed-container acceptance or production proof. No commit/push/deploy occurred.

## Final rebased acceptance evidence (2026-10-01, Bangkok)

After root confirmed protected-main CI success, the candidate was rebased onto
`c56d9cb9b4b2c351aab4893cda2ee89364ef3721`. Only the ten owned files were
preserved/applied, without conflicts. The exact preservation stash
`458caa92befa3e9a03f7df2340c936ea138af626` remains retained; it is not a code
commit or release. No other writer's files were modified.

Fresh sequential private project Release build:0warnings/0errors28.74s. Focus:
33/33passed,0failed/0skipped,1m55s
(`TestResults/wall397-rebased-final-focus/rebased-final-focus.trx`). Full affected
suite:2537/2537passed,0failed/0skipped,10m36s
(`TestResults/wall397-rebased-final-full/rebased-final-full.trx`). This is the
protected-base2516 plus21 new cases; earlier2502 is not new-base acceptance.

The package, interop, new JS test and deterministic bundle SHA256 values above
remain byte-identical after the C# repair and rebase. Root approved reusing that
terminal npm CI result conditional on this readback. Fresh final English/Thai
desktop/mobile screenshots are under `TestResults/wall397-rebased-final/screenshots`;
Thai375 and English1280 were directly inspected for actual mesh foreground and
localized preview-only text. Browser tests additionally assert framebuffer pixels,
worker completion, no price/DFM claim, removal, and no overflow/errors.

Fresh rebased static checks passed: `git diff --check`; scoped
`dotnet format whitespace ... --verify-no-changes --no-restore`; `node --check`
for the interop and new JS tests; package JSON and Thai resource XML/unique-key
parsing; redacted scoped `gitleaks stdin` (114817 bytes, no leaks);
`dotnet list Legacy.Maliev.Web.slnx package --vulnerable --include-transitive
--no-restore` (all four projects, no vulnerabilities); and
`npm audit --include=dev` (zero vulnerabilities). All eight private dependency
clones were rechecked at their exact clean pins. The two main project dependency
clones live under owned `TestResults/.dependencies`; profile clones and their
runtime dependencies live under the same worktree's owned `.dependencies`.

Root owns independent diff/build/test review and protected-main integration.
Full Aspire/container/production acceptance remains explicitly unproven.
