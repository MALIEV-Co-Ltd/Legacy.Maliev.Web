# Standalone native repair-bound mathematics

This verification library computes conservative representation residual bounds.
It does not alter import transfer settings, authorize warning-bearing repairs,
prove source material regions, or validate manufacturing/quotation eligibility.
The importer integration and its policy are separate, unfinished consumers.

## Dependency closure

The independently reviewed slice consists of these four headers and four tests:

- `kernel-repair-bounds.hpp`, `test-repair-native.cpp`
- `kernel-repair-correlated.hpp`, `test-repair-correlated-native.cpp`
- `kernel-repair-endpoints.hpp`, `test-repair-endpoints-native.cpp`
- `kernel-repair-domains.hpp`, `test-repair-domains-native.cpp`

Correlated and endpoints include bounds; domains includes both. Each test includes
its corresponding header. No importer hook, export header, policy helper or
`kernel-repair.hpp` is needed. External dependencies are pinned OCCT and the C++
standard library. Follow `README.md` for the pinned native build environment:
occt-import-js `c2148e54b456b571238d35cac037d304053d64b2`, OCCT
`d2abb6d844231cb8f29be6894440874a4700e4a5`, Emscripten3.1.69.

## Compile before executing each suite

The following commands use a previously completed pinned build in `/build`,
including its generated include response file and retained OCCT objects. Copy
all four headers into `/tmp`. Copy the test being built there too. The retained
development container is named `maliev-cnc-kernel-tolerance`; a newly reproduced
container with the same build inputs is equally suitable.

From inside the container, run this sequence separately for each `NAME` below:

```sh
cd /build
em++ -O1 -std=c++11 -fwasm-exceptions \
  @CMakeFiles/OcctImportJS.dir/includes_CXX.rsp -I/tmp \
  -c /tmp/NAME.cpp -o /build/NAME.o
em++ -O1 -fwasm-exceptions --bind -sSTACK_SIZE=10MB \
  -sALLOW_MEMORY_GROWTH=1 /build/NAME.o \
  @CMakeFiles/OcctImportJS.dir/objects1.rsp \
  @CMakeFiles/OcctImportJS.dir/objects2.rsp \
  @CMakeFiles/OcctImportJS.dir/objects3.rsp -o /build/NAME.js
node /build/NAME.js
```

Use literal substitution for `NAME`, not a filename including `.cpp`:

| NAME | Current passing checks |
|---|---:|
| test-repair-native |38|
| test-repair-correlated-native |24|
| test-repair-endpoints-native |22|
| test-repair-domains-native |36|

The current independently rerun total is120 checks. Every compile/link must
succeed before its Node execution. These tests cover affine/analytic and rational
spline bounds, placement, native periodic/exterior semantics, narrow spikes,
changed geometry, invalid inputs, resource exhaustion and coefficient-cache
equivalence. They are not a replacement for importer integration tests.

## Numerical and resource contract

Bounds use outward binary64 interval arithmetic over retained native
coefficients; unavailable results are not passing certificates. Inputs must be
in one consistent linear unit. Millimetres and the explicit diagnostic budget
0.01mm are test/application choices, not customer tolerance or a library default
manufacturing policy. Do not derive an allowed error from native repair tolerance.

Correlated residual defaults retain32768 visited intervals and depth32; callers
can supply stricter resource limits and a continuation/deadline callback.
Represented surface tensor enumeration is capped at1024 cells; local periodic
normalization is bounded rather than permitting arbitrary distant parameters.
The library does not own the whole-assessment aggregate cap or60-second watchdog;
those belong to integration. Unsupported domains, denominator uncertainty and
resource exhaustion stay unavailable. Emscripten process-CPU measurements are
unavailable/null; monotonic elapsed timing must not be described as CPU time.

Derived coefficient caches require immutable extracted coefficients for the
object lifetime. They cache data, not acceptance decisions, and do not eliminate
independent source correspondence, orientation or material-region obligations.

## Optional shared-parameter composition and fallback

The independently reviewed extension adds `kernel-repair-composition.hpp`,
`kernel-repair-residual.hpp` and `test-repair-composition-native.cpp`. The current
dispatcher also includes the reviewed cylinder and rational headers described
below: its complete header closure is the four base headers plus composition,
cylinder composition, rational composition, high-axis composition and residual
(nine headers). It does
not depend on
the importer hook/export, policy, cone or sphere helpers.

Copy all nine headers and the composition test to `/tmp`, then use the exact
compile/link sequence above with `NAME=test-repair-composition-native`. Running
without arguments executes the public generated fixtures. The recorded30-check
run additionally loaded two private original-geometry fixtures with the test's
optional file arguments (link with `-sNODERAWFS=1` to enable those local reads).
Those fixtures are deliberately not distributed. The correlated24-check suite
was recompiled and rerun against this extension as well.

Composition encloses the complete degree-at-most18 residual polynomial for
nonrational cubic3D/cubic2D/bicubic support representations. Native knot-span
guards and all coefficients are retained; no sampling or affine snapping is
used. Unsupported representations return unavailable and can use the existing
correlated bound. `BoundRepairMetric` accounts failed composition work before
fallback: both phases share one allowance and the same continuation callback.
The optional trailing correlated `intervalLimit` defaults to32768; the wrapper's
`intervalLimit` and `compositionLimit` also default to32768 and cannot increase
that maximum. Zero allowance performs no work. Depth32 and physical-budget
semantics are unchanged. A partial or cancelled attempt never restarts with a
fresh allowance. Dispatch diagnostics describe mathematical methods, not repair
eligibility or source-region acceptance.

Frozen SHA256 for the extension:

| File | SHA256 |
|---|---|
| kernel-repair-correlated.hpp |53A4D9BCA1DF88356BAB8A51F571CECF539A6EC643E8A4C7AA88F9F977C5A674|
| kernel-repair-composition.hpp |AD179AFB706775D1453B85A3A62ADF369F3C8E2541E4C3FE3EA149DD08401CC4|
| kernel-repair-residual.hpp |330D8EBA575B9BA4DF31A7FABE8365C1705F5DD4A41F8794CD669CF2A0A21401|
| test-repair-composition-native.cpp |CDC32830930EDBED2E67B5851E523B81BE4A086DD7DFC3AD1827BBCFBFDA3315|

The current reviewed composition extension also accepts an analytic-line pcurve
with the same nonrational cubic3D/nonperiodic bicubic support. Both exact native
line direction components are kept, including tiny transverse slopes; this is
not an isoparametric approximation. Degree18 storage, rational/periodic rejection,
native span guards, remainder-free polynomial arithmetic and work limits are
unchanged. The current composition suite passes53 checks with four optional
private fixtures (29 public generated checks without them). Covered additions
include rotated affine pcurves, tiny slopes under amplified support, deliberately
snapped/translated negatives, native endpoint extensions and narrow source spikes.
The private coefficients remain undistributed. This slice had a clean whole
native build, all four export gates and importer regression; neither those
checks nor the optional line method certify source material-region eligibility.

## Optional cylindrical and periodic rational composition

The independently reviewed pure helpers are:

- `kernel-repair-cylinder-composition.hpp`, which includes composition.
- `kernel-repair-rational-composition.hpp`, which includes cylinder composition.
- `test-repair-cylinder-composition-native.cpp` and
  `test-repair-rational-composition-native.cpp`.

They do not include the importer hook, export, policy or source-region helpers.
Use the same compile/link/run sequence above, with all nine math headers in
`/tmp` and the corresponding test copied there. Public generated tests require
no data files or Node raw-filesystem flag:

| NAME | Public passing checks | Optional private-fixture run |
|---|---:|---:|
| test-repair-cylinder-composition-native |17|27 with two local fixtures|
| test-repair-rational-composition-native |25|67 with six local fixtures|

The private original coefficients are deliberately not distributed. The public
counts above were executed without arguments, after clean compiled private runs;
both pure helpers and the dispatcher have independent review approval.

The cylinder lane requires nonrational cubic 3D source, affine nonrational spline
pcurve and analytic cylinder support. It keeps the exact native angular affine
polynomial, all Taylor terms through degree8 and the explicit ninth-order
remainder. Placement acts on both polynomial and error. Wider angular pieces
subdivide; unknown/rational source representations remain unsupported.

The initial rational lane requires a nonperiodic-U, periodic-V degree(5,2)
BSpline support, cubic nonrational pcurve and cubic nonrational spline or native
circle source. Homogeneous lift degree21 and residual numerator degree24 retain
every coefficient. The circle model's degree4..8 terms are moved into an explicit
remainder, never discarded. Each periodic knot-cell guard covers the entire
parameter piece and its raw denominator must be positive; positive native
weights do not justify clamping an exterior guard denominator. Candidate center
enclosures are unioned before rejecting witnesses. Uncertain denominators,
distant periodic domains and unsupported degrees stay unavailable. This does
not support arbitrary rational surfaces or the separate nonperiodic C6 classes.

`BoundRepairMetric` selects these exact classes; optional trailing
`cylinderLimit` and `rationalLimit` default to32768 and can only reduce available
work. Failed prefixes, fallback and cancellation share the original comparison
allowance and continuation callback. No fallback regains a fresh allowance.
Diagnostics expose class-specific work and elapsed time, not eligibility.

| File | SHA256 |
|---|---|
| kernel-repair-cylinder-composition.hpp |2ECDA2D6C0C3FFE25DAF134D13C825C43F606490C12A95703059FD2248E0D860|
| test-repair-cylinder-composition-native.cpp |740AAB0AEE265BE4A8812D0076C9CF41218DB524B119128ED716C56647C0E7AC|
| kernel-repair-rational-composition.hpp |C7162870BED25BC3168BC1ACA71C1BCF4E6BAFDD9D6B7A2986D056CBA1F17A38|
| test-repair-rational-composition-native.cpp |58517900587325CD38992F53EA491735D6B790BA377A423F0C0637833A504C84|

The export-dependent dispatch tests are separate integration tests, not part of
this standalone dependency closure. Their passing checks do not establish source
material-region preservation or manufacturing eligibility.

## Optional nonperiodic high-axis rational composition

`kernel-repair-high-axis-composition.hpp` includes rational composition. Its
separate supported class is nonperiodic degree(2,6) or degree(2,8) BSpline
support with positive finite native weights. A cubic nonrational pcurve and
cubic nonrational spline, circle or ellipse source use exact cubic U and an
endpoint-affine V model. The complete omitted V difference is bounded against
the placed surface V derivative over the corridor containing both paths. This
error is added to the source-model remainder; tiny slopes are never snapped.
An analytic-line pcurve with nonrational degree6/8 source uses exact composition.
Ellipse and circle models retain all omitted Taylor terms in explicit bounds.

The existing degree24 storage, raw positive-denominator guard, depth32 and
32768 comparison allowance remain unchanged. Corridor cells and failed nominal
work consume the same allowance as fallback. The optional trailing
`highAxisLimit` can only reduce work; cancellation never grants a fresh budget.
Periodic supports, unsupported source/pcurve classes, uncertain denominators and
exhausted resources remain unavailable. This is not arbitrary rational support.

Copy the nine headers and `test-repair-high-axis-composition-native.cpp` into
`/tmp`, then run the compile/link sequence above with
`NAME=test-repair-high-axis-composition-native`. The public no-argument run
passes17 checks; the reviewed optional six-private-fixture run passes59.
Public cases include amplified tiny-V omission, ellipse tails, raw denominator
failures, a narrow source spike, unsupported periodic support, cancellation and
combined corridor/nominal work limits. Private coefficients are not distributed.
The exporter-dependent dispatch regression is outside this standalone closure.

| File | SHA256 |
|---|---|
| kernel-repair-high-axis-composition.hpp |1526B6100F833B690D05072D2903012059F066F157AD60A1F6ED91D97EF62552|
| test-repair-high-axis-composition-native.cpp |01C3EE9A6567DEE107748E72529AFA56316C0B90CAE3D66FB5456A7A57701F98|
