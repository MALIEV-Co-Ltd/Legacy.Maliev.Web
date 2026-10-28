# Knowledge Center factual audit — 16 October 2026

## Scope and method

Reviewed all 19 Knowledge Center pages, matching Thai resources and the referenced teaching SVGs at baseline `94eaf9bb0571594114b8aaab6335cb446ea9ed2e`. Three independent read-only reviews covered CNC, materials/finishing/metrology, and introductory/additive/scanning content. The parent inspected the findings, verified the correction sources, checked English/Thai edits, and independently recomputed 22 numerical examples.

The review checks public technical guidance against primary manufacturers, standards bodies and national metrology institutions. It does not certify an individual part, machine capability, material batch or production setting. Generated photographs remain explicitly illustrative; they are not dimensional evidence or customer case studies. No unverified numerical MALIEV tolerance, inventory, machine-envelope or delivery guarantee is added.

## Corrections

| Area | Correction | Evidence |
|---|---|---|
| Inspection glossary | Distinguish instrument resolution, accuracy, measurement error, repeatability conditions and measurement uncertainty. Uncertainty describes dispersion rather than simply a min/max range. Identify standard/expanded uncertainty and its coverage basis. | [JCGM VIM 4.14](https://jcgm.bipm.org/vim/en/4.14.html), [2.13](https://jcgm.bipm.org/vim/en/2.13.html), [2.20](https://jcgm.bipm.org/vim/en/2.20.html), [2.26](https://jcgm.bipm.org/vim/en/2.26.html) |
| Scanning glossary | Align accuracy and repeatability with the same metrology definitions; explain that scanner-resolution terminology must be read using the supplier's definition. | JCGM references above; [Artec terminology](https://www.artec3d.com/learning-center/accuracy-precision-resolution) |
| Printing diagram | Layer planes are perpendicular to build Z; stacking progresses along Z. Update English/Thai alt text, labelled viewport, SVG description and caption metadata together. | [Prusa printer operation](https://help.prusa3d.com/article/types-of-printers-and-their-differences_112464?product=mmu3), [Formlabs orientation](https://formlabs.com/support/Model-Orientation/) |
| Printing photo caption | Build orientation changes the layers' orientation relative to the applied load; it does not determine the application's load. | [Prusa design guidance](https://help.prusa3d.com/article/modeling-with-3d-printing-in-mind_164135) |
| Thai tooling terminology | Use เหล็กกล้าความเร็วสูง (HSS) rather than the incomplete เหล็กรอบสูง. | [GESAC Thai tooling catalog](https://www.gesac-tools.com/wp-content/uploads/2025_Hole-Machining_Product-Catalog_TH.pdf) |
| Thai instrument terminology | Translate generic caliper as คาลิเปอร์, without restricting it to a vernier type. | [NPL GPG40](https://www.npl.co.uk/resources/gpgs/callipers-micrometers) |
| Thai process terminology | Translate rough machining as ตัดเฉือนหยาบ rather than narrowing it to rough milling. | [Bodycote stress relieving](https://www.bodycote.com/what-we-do/precision-heat-treatment/stress-relieving-atmosphere-vacuum/) |
| Tool-wear citation | Replace the obsolete thread-milling troubleshooting endpoint with the current milling troubleshooting reference that actually supports the text. | [Sandvik milling troubleshooting](https://www.sandvik.coromant.com/en-us/knowledge/milling/troubleshooting-milling) |

## Page coverage

| Page | Review outcome and primary support |
|---|---|
| Index | Topic directory and imagery introduce the guides without numerical process or capability claims. |
| Guidelines | Matching model/drawing revisions, units, requirements, quantities and sample condition are quotation inputs. STEP/mesh formats are conditional on upload-route support. No unverified supported-format or upload-size guarantee. |
| Workflow | Scope, revision approval, changes and inspection deliverables are explicitly subject to agreement, rather than promises of an undocumented company procedure or fixed schedule. |
| Specifications/Index | Service-input overview separates nominal geometry, specified requirements and observations of an existing sample. No additional universal tolerance claim. |
| Specifications/CNC-Machining | Overview routes readers to bounded design guidance; examples remain illustrative, not MALIEV acceptance limits. |
| CNC/Feeds-and-Speeds | Metric milling/drilling/turning calculations, effective cutting diameter, feed per tooth versus feed per revolution and RPM clamps checked against [Sandvik equations](https://www.sandvik.coromant.com/en-gb/knowledge/machining-formulas-definitions/milling-formulas-definitions) and Haas documentation. Inputs are examples rather than recommended cutting settings. |
| CNC/Cutting-Tools | Tool material, flute count, reach, runout, ball-nose contact, climb milling and coolant caveats checked against Dormer Pramet, Harvey Performance and Sandvik. Terminology and source URL corrected above. |
| CNC/Geometry-and-Workholding | Wall support, internal-corner engagement, moving envelope, residual stress, free-state distortion and chatter claims checked against Sandvik, Autodesk and NIST. Ratios and corner-clearance arithmetic independently recomputed. |
| CNC/Holes-and-Tolerances | Drill-tip/full-thread depth distinction, hole proportions, datums and thermal example checked against Sandvik, ASME and NIST. No universal engagement length or tolerance asserted. |
| CNC/Machining-Processes | Indexed versus simultaneous machining, conductive-material EDM, shaped electrodes, wire taper and grinding damage caveats checked against [Autodesk](https://www.autodesk.com/solutions/5-axis-machining), [Makino](https://www.makino.com/resources/content-library/article/archive/edm-wire-tutorial/198) and [Norton](https://www.nortonabrasives.com/en-us/resources/expertise/5-grinding-considerations-improving-surface-finish). No implied offer of every described process. |
| Materials/Selection | Grade/condition, welded 6061-T6 caution, environment-dependent corrosion and long-term creep distinctions checked against [Hydro](https://www.hydro.com/globalassets/01-products--services/extruded-profiles/americas/ena-resources/alloy-data-sheets/hydro_2019_data_sheet_6061.pdf), [Outokumpu](https://www.outokumpu.com/en/products/corrosion-tables) and [ASTM D2990](https://store.astm.org/standards/d2990). |
| Materials/Metals | Alloy versus temper, product-form-specific 7075 data, 316L welding rationale, 42CrMo4 condition and leaded C36000 checked against [Kaiser](https://online.kaiseraluminum.com/depot/PublicProductInformation/Document/1042/Kaiser_Aluminum_7075_Tube_and_Pipe.pdf), [Outokumpu](https://www.outokumpu.com/de-de/products/product-ranges/supra), [Ovako](https://steelnavigator.ovako.com/steel-grades/42crmo4/) and [CDA](https://alloys.copper.org/alloy/C36000). |
| Materials/Engineering-Plastics | POM-C, nylon moisture, PEEK grade dependence and printed-PC orientation caveats checked against [Ensinger POM](https://www.ensingerplastics.com/en-us/shapes/acetal-tecaform-ah-natural), [Mitsubishi nylon](https://www.mcam.com/en/products/nylons), [Ensinger PEEK](https://www.ensingerplastics.com/en-sg/shapes/tecapeek-natural) and [Stratasys PC](https://www.stratasys.com/siteassets/materials/materials-catalog/fdm-materials/pc/mds_fdm_pc_0823a.pdf?v=4a7074). |
| Manufacturing/Process-Selection | Geometry/material/tooling trade-offs are conditional. The attributed Epson CNC-to-injection-molding example directly matches [Protolabs' account](https://www.protolabs.com/en-gb/resources/blog/how-to-select-the-best-manufacturing-process-for-your-part/); it is not presented as a universal cost threshold. |
| Finishing/Surface-Finishes | Anodizing penetration versus outward build-up checked against the [Aluminum Anodizers Council](https://www.anodizing.org/anodizing-reference-guide/). The assumed 20 µm hardcoat example yields 10 µm outward per surface, OD +20 µm and bore −20 µm. Process/pretreatment caveats remain. |
| Finishing/Heat-Treatment | Annealing versus stress relief, quench/temper versus precipitation hardening, diffusion treatment versus coating and non-universal sequencing checked against [Bodycote](https://www.bodycote.com/what-we-do/precision-heat-treatment/stress-relieving-atmosphere-vacuum/). Thai machining term corrected above. |
| Inspection/Measuring-Parts | Instrument/setup caveats, 20 °C reference, thermal correction, datums and agreed acceptance decisions checked against NPL, [NIST](https://www.nist.gov/publications/uncertainties-dimensional-measurements-made-nonstandard-temperatures), Mitutoyo and JCGM. Terminology corrected above. |
| Specifications/3D-Printing | Mesh geometry, layer height versus tolerance, orientation, support removal and resin drainage checked against Prusa and [Formlabs](https://formlabs.com/support/Hollowing-models-in-PreForm/). Layer wording corrected above. |
| Specifications/3D-Scanning | Mesh versus editable CAD, measured versus reconstructed regions, optical access, preparation, accuracy and repeatability checked against [Artec processing documentation](https://docs.artec3d.com/as/20/en/process.html) and JCGM. Definitions corrected above. |

## Independent numerical checks

Twenty-two calculations passed: spindle speed, linear feed and actual cutting speed for the milling/drilling/turning examples; chip loading after an RPM reduction; wall, tool-reach and hole-depth ratios; R4 corner tool-centre clearance; both thermal-growth examples; and the assumed anodize diameter growth. Original full-precision calculations and published rounding are retained in the off-repository `knowledge-facts/arithmetic.json` receipt.

Key results: milling 3819.7 rpm / 611.2 mm/min, capped feed 480 mm/min; drilling 2387.3 rpm / 286.5 mm/min, capped feed 240 mm/min; thermal growth 0.023 mm and 0.012 mm. No production settings were inferred from these assumed inputs.

## Retrieval limitations

The original Haas operator-manual PDF returned HTTP 403 to one audit fetch. G96/G50 claims were corroborated using the directly retrieved [official Haas Lathe Programming Workbook](https://www.haascnc.com/content/dam/haascnc/en/service/reference/programming-workbooks/lathe---programming-workbook.pdf), printed pages 49–50. Individual G96/G50 pages were indexed but direct tool opening failed. The legacy NIST SP1006 PDF had a certificate retrieval problem in one tool; the same publication was corroborated through indexed publisher text for the [official NCNR FY2003 annual report](https://www.ncnr.nist.gov/AnnualReport/FY2003/NCNR_2003_all.pdf), printed pages 34–35. Direct opening of that alternate PDF also failed, so it is recorded as indexed corroboration, not a successful direct PDF read. No TLS validation was bypassed. A Renishaw resource-centre download produced a tool retrieval error; that is not evidence that the document or URL is false. Its publication identity is visible from Renishaw's metrology-fixturing page, but the inaccessible download was not treated as independently inspected evidence.

These limitations concern individual retrieval routes, not numerical findings. Exact material grade/condition, acceptance criteria, treatment and machining parameters still require project-specific engineering review; generic guidance does not establish a finished-part guarantee.

## Validation

- 19-page English/Thai resource parity, nonempty values, XML/SVG/JSON parsing and whitespace checks: passed.
- 22 independent numerical calculations: passed.
- Release build: zero warnings, zero errors.
- Focused source, localization, shared-shell, SEO and browser checks: 49 passed, zero failed or skipped.
- Full affected Web suite: 1192 passed, zero failed or skipped, 8m46s.
- Live loopback preview checks: corrected English/Thai metrology prose, printing direction, Thai HSS terminology and SVG description all returned HTTP 200 with the corrected content.
- All 14 observed processes belonging to the full test run exited; no owned test workers remain. A separate loopback preview is retained with a finite lease until 21:55 Bangkok time on 2026-10-16.
- C# formatting and Node suites are not rerun: no C# or JavaScript source changed; whitespace/XML checks and the native rendering suite cover this prose/resource change.
- No C# or JavaScript logic changed; no new implementation-mirroring string tests added for prose edits. Existing localization and browser coverage exercises the corrected pages.
- No push or deployment performed.

## Validation commands and resource scope

```powershell
dotnet build Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false /nodeReuse:false -warnaserror
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~KnowledgeCenterBrowserTests|FullyQualifiedName~KnowledgePageMigrationSourceTests|FullyQualifiedName~ApplicationAreaShellSourceTests|FullyQualifiedName~TechnicalSeoTests|FullyQualifiedName~SeoBusinessContractTests|FullyQualifiedName~ApplicationPageMigrationSourceTests'
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type none
git diff --check
```

Native stages run serially with the unchanged 4 GiB free-RAM admission guard, two logical processors, a 2 GiB managed heap cap and finite process leases. Source changes cover Razor text, Thai resources, SVG description metadata and its caption manifest; no API, authorization, database, quotation-calculation or deployment contract changes. Existing browser coverage reads all 19 guides in both languages at desktop and mobile widths. Logs, TRX output, screenshots, arithmetic receipt and ownership/cleanup ledger are retained under `D:/codex-temp/2026-10-14/task/knowledge-facts`.
