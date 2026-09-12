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

## License

occt-import-js declares LGPL-2.1 in its package metadata; this extension follows
that license. OCCT is LGPL-2.1 with
its additional exception; preserve its `LICENSE_LGPL_21.txt` and
`OCCT_LGPL_EXCEPTION.txt` from the pinned checkout when distributing a binary.
The complete corresponding patched source is reproducible using the pins and
overlay above. Do not redistribute the candidate without the upstream license
texts and corresponding-source access required by these licenses.
