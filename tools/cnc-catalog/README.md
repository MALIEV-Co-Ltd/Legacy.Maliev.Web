# Local CNC tool catalog

`catalog.json` stores 83 source-verified catalog rows checked on 2026-09-06,
covering all nine requested tool families. It is a local review asset, not the
application's active tool library. Every record has `machiningAuthorized: false`.
No catalog listing proves MALIEV owns the tool, that Thailand stock is available,
or that a holder, insert grade, material, feed, speed or toolpath is qualified.

## Contents and coverage

| Family | Verified local rows | Coverage limits |
| --- | ---: | --- |
| Face mill | 1 | Mitsubishi ASX445-050A04R, DC50/DCX63; arbor and inserts unresolved |
| Flat end mill, standard | 13 | Discrete MISUMI D1-16 rows; D11/13/15 not verified |
| Flat end mill, long neck | 15 | Selected MISUMI D1-6/8/10/12 lengths; D16 and other sizes unverified |
| Ball end mill | 12 | NS TOOL ALB225 D1-10 plus selected necks; D7/9 unverified |
| Spot drill | 3 | OSG 90-degree D10/12/16 |
| HSS drill | 16 | NACHI SDP D1-12 integer rows plus 4.2/6.8/8.5/10.2 |
| Engraving V bit | 1 | Harvey 993230, 60 degrees; tip/maximum cutter diameter unknown |
| Metric tap | 13 | Selected OSG M3-M16 pitches; all others remain unverified |
| Imperial tap | 6 | OSG 1/4, 5/16, 3/8 UNC and UNF |
| Thread mill | 3 | OSG internal UNC/UNF plus Harvey metric single-form; M14x1 pairing unverified |

The machine-readable `requestedCoverage` preserves requested ranges and gaps.
`catalogued` means that small requested catalog subset is present; it does not
authorize operation selection. `sampled` and `partial` must not be expanded by
interpolation. Internal MISUMI row IDs are not invented SKUs: `orderCode` is null
until the applicable order-code configuration has been verified.

## Fact and assumption boundary

- All dimensions are millimetres; angles are degrees. Inch dimensions use exactly
  25.4 mm/in. Imperial pitch uses 25.4/TPI and keeps the source TPI/designation.
- `sourceEvidence` names the primary source, table/page/row and the exact fields
  it supports. `checkedOn` is our consultation date, not the source publication
  date or proof of inventory freshness. Each source retains its original URL.
- Null means not verified/not stated. It is never zero, equal to the diameter,
  or evidence of a safe cylindrical envelope.
- Flute length, under-neck length, thread length and assembled projection have
  different meanings. A MISUMI D1/LU30 tool has a 1.5 mm flute, not 30 mm. A
  NACHI SDP12.0 has flute111/overall149/point3.6, but no separately catalogued
  shank dimension in this source. The spot drill's flute length is not its usable
  spotting cone depth. The minimum subsequent drill size is not the tip diameter.
- Mitsubishi LF40 is `functionalLengthMm`; DC50 is the cutting reference diameter,
  DCX63 the maximum cutting diameter, and DCON22 the connection bore. None is an
  assembled holder projection or shank diameter. Insert grade remains unknown.
- Tap nominal thread diameter is in `thread.nominalDiameterMm`, while an exact
  cutter envelope remains null. Thread length is not a certified usable depth.
- NS TOOL labels neck taper angles as reference values requiring measurement;
  they were not promoted into verified clearance geometry. No neck transition
  shape or complete holder envelope is inferred.
- The existing spot-drill D25 holder / 30 mm engagement convention is retained
  only in `quotingAssemblyAssumptions`, marked unverified and non-authorizing.
  It is not merged into manufacturer facts and no projection is generated.
- No feeds, speeds, prices, stock counts, purchase actions or broad material
  compatibility claims are included. Product family notes are not machining
  feasibility certificates.

## Validation and integration

```powershell
node tools/cnc-catalog/validate.cjs
node --test Legacy.Maliev.Web.Tests/JavaScript/cnc-local-catalog.test.cjs
```

The dependency-free validator executes the exact JSON Schema keyword subset
used by `catalog.schema.json`, rejecting unsupported keywords. It also verifies
source allowlisting and dates, field provenance, units/geometry consistency,
duplicates, coverage references and the non-authorizing boundary. Schema validity
checks transcription structure, not the truth of an arbitrary new source claim;
new rows still require source review.

This slice does not edit `cnc-tool-library.js`, compiler, kernel or application
assets. Root owns source review, application integration and final commits.
No .NET build applies to this standalone JSON/CommonJS asset; syntax checks,
schema/semantic validation and the catalog regression suite are its validation
targets. Existing app tests do not demonstrate this catalog is integrated.

## Source inspection notes

MISUMI Thailand standard/long-neck product tables and its long-neck PDF were
checked separately. OSG official PDFs supplied spot, tap and thread-mill rows.
The NS TOOL official indexed current product table was readable, while direct
page fetching timed out; that retrieval limitation is recorded on its source.
Mitsubishi's English product page supplied named ISO dimension fields.
Harvey's product page supplied the engraving dimensions; web thickness was
deliberately not treated as tip diameter.

The NACHI official URL is a 30 MB, 883-page catalog. The web PDF reader failed;
it was downloaded to ignored `.scratch/`, then physical PDF page 742 (printed
G-6) was text-extracted and rendered for visual verification of merged cells.
The PDF and rendered image are scratch evidence, not repository deliverables.
No customer CAD or private source material was added.

## Executed checks

Initial `node --test Legacy.Maliev.Web.Tests/JavaScript/cnc-local-catalog.test.cjs`
failed as expected because catalog.json did not exist. After implementation:

- `node --check tools/cnc-catalog/validate.cjs`: passed.
- `node --check Legacy.Maliev.Web.Tests/JavaScript/cnc-local-catalog.test.cjs`: passed.
- `node tools/cnc-catalog/validate.cjs`: 83 records / 14 sources passed.
- Catalog regression suite: 16 tests passed, zero failed or skipped.

The bounded extension adds MISUMI VAC-PEM2LB D8/LU42, D10/LU45 and D12/LU50
with independently stated flute lengths 12/15/20 mm. Neck diameters stay null;
D16 long-neck remains unverified. Harvey826530 adds a metric single-form sample
with verified internal/external family support in `threadUsage`; unknown numeric
pitch limits stay null. `maximumThreadDepthMm` is the catalog depth label, not
flute length or measured under-neck length. M14x1 external suitability remains
unverified because no checked source establishes that exact pairing or pitch
bounds. The Harvey family introduction says metric while a generic bullet says
UN; that inconsistency is retained in the record, not silently resolved. Three
extension assertions failed before the new records, then all 15 tests passed.
A missing-family/duplicate-coverage mutation failed before the validator guard,
then the full 16-test catalog suite passed.

No .NET build, application suite or browser gate was run by this catalog task,
because this standalone asset is not wired into the application. No commits,
purchases, deployment or external messages were made.
