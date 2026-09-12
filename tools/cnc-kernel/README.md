# Direct OCCT face exporter (staged)

This build adds `MalievKernelFaces.v1` provenance and direct face/body IDs to
the existing `ReadStepFile`, `ReadIgesFile`, `ReadBrepFile` results. Face IDs and
triangle ranges are emitted in the **same callback** from the exact `TopoDS_Face`
used for triangulation. IDs are local to one import result; consumers must pair
them with their import revision and must not treat them as persistent CAD names.

Current scope: oriented analytic planes/cylinders/cones/spheres/tori, resolved
support placement, BRep bounds without triangulation, and explicit unsupported
surface types, ordered native trims and shared source-edge adjacency. This is **not**
a complete CadDocument and cannot independently authorize manufacturing.
Body IDs identify upstream mesh groups (solids, isolated shells, or standalone
face groups); they do not assert that every mesh group is a closed solid.

Coordinates follow the existing import API. Request `linearUnit: 'millimeter'`
(the default) for millimetres. BREP has no intrinsic unit declaration; its unit
must be established by the caller. Non-mm imports are marked and must not be
consumed as normalized millimetres. `placement3x4` describes the face location;
support and bounds already include it, so do not apply it twice.

`bounds` remains the geometric `AddOptimal` result without triangulation or
shape-tolerance expansion. `toleranceBounds` is a separate conservative box
expanded by the largest native face/edge/vertex tolerance. Both use import-world
units. `precision` exports those native tolerances, the stored triangulation
deflection, and the maximum distance of each mesh node from its surface evaluated
at that node's native UV. The latter is a measured node discrepancy, not a bound
on triangle interiors, a machining allowance, or proof of CAD accuracy. Missing
triangulation/UV data yields explicit unavailable diagnostics. Consumers must
assess native tolerances against their own required accuracy; a successful import
does not mean zero geometric error. In particular, the bracket fixture's native
vertex 107 is 0.0022045454 mm off face 42's plane, within its 0.0027718602 mm native
vertex tolerance. Do not force that mesh node onto the support or inflate the
exact bounds to hide the distinction.

## Pins and build

- occt-import-js 0.0.23: `c2148e54b456b571238d35cac037d304053d64b2`
- OCCT submodule: `d2abb6d844231cb8f29be6894440874a4700e4a5`
- Emscripten 3.1.69 image: `emscripten/emsdk@sha256:9d6522879357a363ada61862481cc12c5f772d5e9738b8addf95d38490cdc6ea`
- Public source: https://github.com/kovacsv/occt-import-js
- OCCT mirror: https://github.com/Open-Cascade-SAS/OCCT (exact same commit;
  original git.dev.opencascade.org endpoint was unresolvable during this build).

The build creates its own isolated pinned checkout inside the container. Run:

```powershell
./tools/cnc-kernel/build.ps1 -Output <ignored-candidate-directory>
node tools/cnc-kernel/test-export.cjs <candidate-directory>/occt-import-js.js <scratch-checkout>/test/testfiles
node tools/cnc-kernel/test-trims.cjs <candidate-directory>/occt-import-js.js <scratch-checkout>/test/testfiles
node tools/cnc-kernel/test-bodies.cjs <candidate-directory>/occt-import-js.js <scratch-checkout>/test/testfiles
```

The script pins the image digest, checks both Git revisions, restricts compilation
to two CPUs, preserves the container for inspection, and never deploys artifacts.
Use a distinct `-Container` name for a fresh reproducibility build. Compare both
artifact hashes from independent builds before claiming byte reproducibility.

The overlay aborts on source drift or repeat application; use a fresh checkout
for rebuilding the overlay. Existing APIs remain callable and existing output
fields are unchanged.

## Native trim schema

Each mesh has `kernelTopology` (`MalievKernelTopology.v1`) with `edges`, `vertices`
and `status: complete|partial`. This status covers only the enumerated faces'
trim facts. It is not solid/shell validation or assembly occurrence coverage;
`kernelProvenance.documentCoverage` records that separate bounded claim. A context lives for one
mesh enumeration inside one import. IDs use OCCT TShape plus location identity,
ignoring orientation, never coordinates. Distinct coincident edges stay distinct;
the same edge across different mesh-group occurrences intentionally gets separate
IDs. Cross-mesh adjacency and persistent names are not claimed.

`face.trims` contains `status`, native `surfaceBasis`, its separate
`surfaceToImportWorld3x4`, and ordered `wires`. Faces are normalized to FORWARD
for trim traversal; original `face.orientation` is preserved. Each wire includes
role, orientation, OCCT `closed` flag, independently checked `connectedClosed`,
`complete`, direct/visited edge-use counts and ordered `coedges`. Every occurrence
has its own `coedgeId`, `edgeId`, orientation, endpoint vertex IDs, pcurve,
native range, seam flag and `isStored`. REVERSED uses traverse last to first;
UV values are never wrapped. Seam uses are queried with their own orientations.
Wire explorer output is compared with direct oriented child occurrences, including
multiplicity. Missing uses, missing pcurves, unsupported geometry and exceptions
make the wire/face/context partial, even when a useful prefix was exported.

Edges expose native `curve3d`, `curveToImportWorld3x4`, native range, FORWARD
endpoints, degeneracy, tolerance, SameParameter/SameRange and all collected uses.
Vertices expose `worldPoint` and tolerance. Adjacency is finalized after all face
callbacks: boundary, same-face-seam, same-face-repeated, two-face or nonmanifold.
When any traversal is partial, adjacencyStatus is partial throughout that mesh;
the classification then describes observed uses, not proof of all ownership.

Exact curve forms are line, circle, ellipse, hyperbola, parabola, Bezier and
BSpline. Spline records preserve poles, weights, degree, knots, multiplicities
and periodicity; 2D points have two coordinates. Native surface bases support
plane, cylinder, cone, sphere, torus, Bezier and BSpline (U-major pole rows).
Unsupported offset/revolution/extrusion forms remain unavailable. Trim wrappers
recognized by OCCT adaptors keep native parameters and the separate trim range.
Apply each native curve/surface placement once; world vertices and face supports
already include their placements. Parameter values and tolerances follow native
OCCT/import units, not an assumed millimetre source for BREP.

The trim test is a candidate acceptance gate, not evidence it passed before a
linked candidate exists. Degenerate-edge, malformed-wire, distinct-coincident
edge and rational/periodic spline numeric fixtures remain additional required
validation before complete coverage is asserted.

## Native body diagnostics

Each mesh also has `kernelBody` (`MalievKernelBody.v1`). It distinguishes a native
solid, standalone shell and standalone face group using the exact importer
source, not a bounding-box guess. Oriented source-face occurrence membership is
matched to native shells, preserving ambiguous or missing matches. The solid ID
is null for shell/face groups. IDs remain import-local.

For native solids/shells it reports `BRepCheck_Analyzer` validity and native shell
closure/orientation checks. `boundedSolidEvidence` additionally requires one
shell, complete membership and an OUT infinite-point classification. Multi-shell
cavity nesting and assembly completeness remain unverified. A closed standalone
shell never becomes a solid merely because its boundary closes. Standalone face
groups do not inherit validity from their enclosing compound. These diagnostics
are not a machining authorization or a complete CadDocument.

## Bounded source document coverage

`kernelProvenance.documentCoverage` has schema `MalievKernelDocument.v1`.
The inventory runs before `XcafRootNode::GetChildren` can omit failed
triangulations. It retains every XCAF free root, assembly/reference ancestry,
definition labels, local/resolved world placements, and leaf face occurrences.
Native `IsEqual` (TShape, location, orientation) associates the exact emitted
mesh source and face callbacks; ambiguous candidates remain explicit. No
coordinate matching or face-support scoring is used. Source-face records keep
emitted IDs and triangle counts, including zero. IDs and labels are import-local.
The source STEP transfer audit separately checks face TShape identity before
placement with `IsPartner`, root transfer results, diagnostics, unsupported
representation items, orphan geometry/topology and declared length units.
`sourceTransfer.diagnostics` retains every native warning/failure with its source
entity number, severity, exact final and original message strings. The category
is explicitly `unclassified-native-transfer`; no warning is assumed harmless.

`completeCadDocument=true` means only complete coverage within this schema's
supported single-solid STEP lane: one source transfer root, no transfer
warnings/failures/lost faces, one supported solid representation item, declared
source units with millimeter output, one resolved solid leaf (possibly under
assembly wrappers), one uniquely associated body, and every source face mapped
exactly once. Every retained reason blocks this boolean. It does not certify
geometric validity, trim consumer capability, shell cavity nesting, tessellation
accuracy, healing, manufacturability, setups or prices. Body/trim diagnostics
remain separate and mandatory for their own consumers.

Multiple roots/occurrences, open shells/faces, non-face source geometry and
ambiguous associations remain review-required. Coincident definition references
are retained separately even when flattened meshes cannot distinguish them.
IGES reports live transfer root/missing-root and diagnostic counts with explicit
`iges_source_entity_coverage_unverified`; its dependent/blanked/group entity
ownership audit is not implemented. BREP document enumeration and source units
remain unverified. No additional sewing/healing is performed; the existing
OCCT importer default transfer behavior is unchanged.

Run `node tools/cnc-kernel/test-document.cjs <candidate.js> <upstream-testfiles>`.
An optional fourth argument is the authorized private originals directory;
the script verifies manifest SHA256 values and prints only coverage inventory.
It reads originals in place and does not copy them. This is not planning success.

The document overlay can be applied once to the retained pre-document native
source with `node tools/cnc-kernel/apply-document-overlay.cjs /src`; fresh builds
invoke it from `apply-overlay.cjs`. After copying updated headers to the retained
container, touch all consuming translation units before the incremental build
because `docker cp` may preserve older mtimes. Keep old objects/containers.

## Opt-in numerical target sessions

`ReadStepFileWithTarget(bytes, params)` uses the existing STEP load/transfer once
and adds `nativeTargetSession` to its ordinary result. Existing import APIs do
not retain a session. Admission requires normalized millimetres, complete native
source-interpretation prerequisites, exactly one valid solid with one checked
shell, complete oriented source-face allocation, and nonrejected infinite-point
OUT. It does not assert the application's private interpretation approval.
Open/multiple/nested-shell/unaccepted sources remain unavailable without repair.

The retained values own the original solid, location/orientation, source faces
and IDs, a complete finite-face boundary container, classifier, distance helper,
and exact imported bytes. No pointer address is serialized. The registry admits
at most four live sessions and 64 MiB aggregate source snapshots, rejecting a
new session at capacity. These are not native-heap or WASM-memory bounds. IDs
increase within one module lifetime; future callers must also bind a worker
incarnation because another module can repeat the string.

Bind once using `BindNativeTargetSource(sessionId, bytes, binding)`, where binding
has contract `NativeTargetSourceBinding.v1`, nonempty `sourceGeneration`,
`nativeImportRevision`, `topologyRevision`, and a lowercase 64-hex
`sourceBytesHash`. Native code compares actual bytes, not a claimed digest.
`byteEqualityVerified:true` and
`bindingAuthority:'caller-labels-bound-to-exact-imported-bytes'` mean exactly
that: these immutable labels are caller correlations, not native SHA validation.
Identical rebinding is idempotent; different bytes/labels reject.

`QueryNativeTargetBatch(request)` accepts this ordered, synchronous contract:

```js
{
  contract: 'NativeTargetQueryBatch.v1', sessionId, binding,
  batchId: 'batch-1', policyVersion: 'native-target-numerical-v1',
  mode: 'membership-and-distance', // or membership / boundary-distance
  coordinateSpace: 'import-world-mm',
  points: [{ pointId: 'p0', point: [1, 2, 3] }]
}
```

The complete batch (1–256 finite points, unique IDs) is copied/validated before
native evaluation. Bad identity, labels, mode, coordinate space, extra inventory
fields or nonfinite values reject without evaluating a prefix. Results echo
every point/ID in order and the producer-owned body/all-face inventory. Per-point
components have available/unavailable/not-requested status; failed components
never become zero distance or empty material. Interrupted suffixes remain
explicit unavailable slots. Batch status is complete/partial/unavailable, with
elapsed time and evaluated count as diagnostics, not completion guarantees.

Membership uses the loaded `BRepClass3d_SolidClassifier`. Rejected, uncompleted or
unexpected states are UNKNOWN. Finite OUT additionally requires a unique actual
source-face witness: public nonrejected OUT with a null face can hide an internal
uncompleted state and stays unavailable. IN/ON are numerical observations, not
proof of all hidden subqueries; ON is not relabeled empty. Infinite-point
admission uses its separate pinned control-flow predicate. No enclosure-based
OUT shortcut is implemented.

Distance uses `BRepExtrema_DistShapeShape` from a transient point vertex to a
compound of **all original finite faces**, never to the solid interior or an
unrestricted support. It requires successful completion, solutions and finite
nonnegative value without an inner-solid solution. Nearest face/edge/vertex IDs
are joined to retained source identities; missing/ambiguous optional support
metadata is explicit. Classification tolerance and distance deflection both use
pinned `Precision::Confusion()` and are separate from maximum source topology
tolerance. Results have `formalIntervalCertificate:false`,
`certifiedDistanceErrorBoundMm:null`, `applicationInterpretationApproved:false`
and `machiningAuthorized:false`. No error interval or quotation admission is
implied. Synchronous native calls have no fictitious cancellation/deadline
guarantee; future worker termination/async brokerage is outside this producer.

`ReleaseNativeTarget(sessionId)` releases owning resources; repeated or unknown
IDs return `released:false`. Failed/provisional imports cannot leak a live slot.
Release does not promise that WASM linear-memory high-water allocation shrinks.

Build with the existing isolated `build.ps1` workflow above; do not publish a
candidate or change pins implicitly. Native test staging must contain the two
new headers, `test-target-query-native.cpp`, and hash-bound public
`native-common-route-cuboid.step`. The harness verifies staged headers against
the actual compiled source and links the existing OCCT/importer object response
files without a second js-interface binding object:

```text
node tools/cnc-kernel/test-target-query-overlay.cjs
sh tools/cnc-kernel/test-target-query-native.sh STAGED_NATIVE_TEST_SOURCE_DIR ISOLATED_RESULT_DIR
node tools/cnc-kernel/test-target-query.cjs CANDIDATE_JS PUBLIC_FIXTURE_ROOT
```

The API test optionally saves write-once raw receipts beneath
`CNC_TARGET_QUERY_RECEIPT_DIR`. It compares all deterministic old output fields;
only measured phase/counter/mark/dispatch timings, the timing-ranked top-20
`audit.slowMetrics`, and the existing increasing native invocation nonce are
excluded. Each raw timing-ranked row is checked against its own complete native
semantic/residual ledger, which remains in exact equality. These test exclusions
never rewrite producer outputs or source/session labels.

## Opt-in native target-query profiling (diagnostic only)

The default overlay and runtime expose no profiling API. For a separately
identified local composed source/build, apply the default overlays first, then:

```text
node tools/cnc-kernel/test-target-query-profile-overlay.cjs PRISTINE_COMPOSED_ROOT
node tools/cnc-kernel/apply-target-query-profile-overlay.cjs ISOLATED_COMPOSED_ROOT
cmake --build ISOLATED_BUILD --parallel 2
node tools/cnc-kernel/test-target-query-profile.cjs BASELINE_JS PROFILE_JS SOURCE_PATH BATCH_MANIFEST RESULT_DIRECTORY
```

The opt-in overlay checks both pinned upstream revisions, all ten exact source
hashes and every replacement anchor before writing. It copies the same bounded
header beside the importer and BRepClass3d sources; IntCurvesFace includes that
exact BRepClass3d copy, as does the guarded 3D generic specialization. The selector header/source add box rejection, edge
preparation, ExtCC construction, existing-result and vertex phases. Box rejection
still tests the original infinite line; ExtCC still uses the original finite
adaptor/BRep domains. Return values, ordered parameters and parallel validity
flags are not changed. Curve-type ExtCC buckets are separate from surface-type
projection/intersector buckets.

Rebuild the changed OCCT caller objects, importer binding object and every
transitive selector-header consumer, retaining and hash-verifying the other
objects. Do not assume old partial CMake dependency records cover reused
objects: enumerate source includes, verify consumers with compiler dependency
output and explicitly rebuild that set using the recorded compiler flags before
the normal CMake link. Do not reuse a
build whose absolute CMake paths, source closure or compiler flags differ. Do not
publish these diagnostic artifacts or overwrite a production pin.

`QueryNativeTargetBatchProfile(ordinaryRequest, diagnosticRequestHash)` calls the
unchanged strict ordinary query under an active synchronous batch context.
`ReadLastNativeTargetQueryProfile()` returns the last bounded sidecar. The hash is
only a caller label: the harness computes SHA-256 independently and checks exact
source/session/batch/mode/point-count correlation. Invalid/truncated labels,
counter overflow or nesting overflow make attribution unavailable, not geometry.
Ordinary output fields and OUT-witness requirements remain unchanged.

Counters use fixed arrays, a 32-frame stack and `steady_clock`, with no clock or
allocation in inactive scopes. Sidecars contain no per-point/per-face records.
Phase inclusive times overlap; sum exclusive descendants plus batch self, never
sum inclusive phases as total. Support buckets are an overlapping breakdown,
not additional exclusive work. After adding selector children, line-tree self
time is residual traversal/control/instrumentation cost, not a measurement of
pure UBTree traversal. Five additional 3D generic ExtCC phases separate each
original adaptor length, remaining preparation (prepare exclusive), interval
finder calls and whole generic solve. Generic solve exclusive is residual
loop/result/status work, not preparation. Two fixed support groups attribute
lengths by actual curve rank/type; generic callers need not be selector edges.
No calculation is cached or accelerated here.
This instrumentation assumes the existing
single-threaded synchronous WASM execution and does not claim multithread safety.

Compile `test-target-query-profile.cpp` twice: once with `PROFILE_HELPER_TU` and
the BRepClass3d header include path, once with `PROFILE_NATIVE` and the importer
include path plus the actual build's `includes_CXX.rsp`. Link both objects with
the existing native object response closure, excluding exactly the js-interface
object as in `test-target-query-native.sh`. The test covers disabled/reset/nested
contexts, unwind and overflow behavior, and actual linked OCCT/importer sharing.
It also solves a finite 3D Bezier/line case with active and inactive hooks and
an equivalent 2D case with no 3D counters. The shared Extrema_GenExtCC.gxx hooks
are guarded by a marker defined/undefined only around the 3D Extrema_ECC_0.cxx
include. Rebuild both 3D and 2D specializations plus every profile-header
consumer; verify the unchanged 2D source/object and ordinary solve results.

The replay manifest binds source bytes, both JS/WASM hashes, ordered batches and
each points-array SHA-256. Maximum 16 batches per source, 256 points per batch,
and 32 KiB per sidecar. Baseline and profile each import the source once; raw
ordinary results and sidecars are written to a new result directory. Complete
ordinary query comparison excludes only `elapsedMs`; existing import comparison
uses the explicitly enumerated timing/invocation exclusions above. A profile is
neither a numerical error certificate nor source, machining or quote authority.

## License

occt-import-js declares LGPL-2.1 in its package metadata; this extension follows
that license. OCCT is LGPL-2.1 with
its additional exception; preserve its `LICENSE_LGPL_21.txt` and
`OCCT_LGPL_EXCEPTION.txt` from the pinned checkout when distributing a binary.
The complete corresponding patched source is reproducible using the pins and
overlay above. Do not redistribute the candidate without the upstream license
texts and corresponding-source access required by these licenses.
