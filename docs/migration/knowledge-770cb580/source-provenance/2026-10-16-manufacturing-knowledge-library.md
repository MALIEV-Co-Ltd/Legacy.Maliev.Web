# Manufacturing knowledge library expansion

## Scope

Local implementation on `codex/manufacturing-knowledge-library-20261016`, based on production `e1edc6de1537712f52c016318e311c8dd3819e5c`. This expansion is not pushed or deployed.

The public Knowledge Center grows from 7 to 19 English/Thai pages. Existing URLs and CNC fragment identifiers remain available. Detailed CNC sections move into focused guides; the original CNC page becomes the overview linking those guides.

## Reading structure

The left navigation uses five native disclosure groups: Start here; CNC machining; Materials and applications; 3D printing and scanning; Finishing and inspection. The current group opens automatically, the current page has `aria-current="page"`, and links to its section headings appear beneath it. Native topic groups and each article's own contents work without JavaScript. The mobile drawer retains its Escape, focus trapping and inert-background behavior; section selection closes it and focuses the target heading's section.

No new breakpoint: the existing 72rem workspace breakpoint controls desktop sidebar versus mobile drawer. The existing sticky, independently scrolling sidebar accommodates long contents. The library directory uses responsive editorial rows with topic descriptions.

## Added guides

- CNC: feeds and speeds; cutting tools; geometry and workholding; holes and tolerances; 3-axis, 5-axis, EDM and grinding.
- Materials: selection checklist; metals and applications; engineering plastics.
- Manufacturing: process selection, including combined routes and project economics.
- Finishing: surface finishes and coatings; heat treatment and material condition.
- Inspection: measurement, uncertainty, datums, acceptance and reports.

Feeds and speeds covers metric units, effective cutting diameter, RPM/feed equations, milling, drilling, turning, RPM clamps, engagement and chip thinning. Worked numerical inputs are explicitly illustrative rather than production settings. The examples were independently recalculated.

Material articles distinguish exact grade, temper, supply form, moisture, creep, environment and verification; they do not promise inventory or blanket food/medical suitability. Process and finishing examples do not claim unconfirmed MALIEV capabilities.

## Research and compatibility

Primary technical sources are linked alongside claims in both languages: Sandvik Coromant, Harvey Performance, Haas, Hydro, Kaiser, Outokumpu, Ovako, BÃ–HLER, Copper Development Association, Ensinger, Mitsubishi Chemical Advanced Materials, INEOS Styrolution, Stratasys, ASTM, ASME, NPL, NIST, Mitutoyo, Renishaw and Bodycote. Supplier process guidance includes Protolabs and Xometry. Product- and supply-form-specific data is not generalized into universal material properties.

The public search catalog, production SEO verifier, physical page classifications and matching test inventories all include the 12 additional routes. There are no changes to APIs, authorization, database schemas, quotation calculations or deployment configuration.

## Validation

- Release build: zero warnings, zero errors before browser testing.
- Final focused source, SEO, shared shell and browser checks: 47 passed, zero failures or skips.
- Browser sweep: all 19 pages in English and Thai at 375px and 1440px, HTTP status, localization, active navigation, valid section anchors, overflow, accessible tables, image decode and button contrast in normal/hover/focus states.
- Additional browser tests: native disclosures via keyboard, mobile and desktop section navigation, mobile focus trapping, Escape and resize cleanup.
- JavaScript suite: 38 passed. Frontend suite: 78 passed.
- Production SEO PowerShell contract suite: 34 passed.
- XML/resource parity, unique IDs, local anchors, retained CNC fragments and worked arithmetic: passed.
- Source-link audit: 74 distinct HTTPS sources; 64 returned HTTP 200 directly. Ten direct checks hit certificate, timeout or automated-access restrictions; the publications were confirmed through publisher pages or indexed primary documents. The NPL PDF link was replaced by its accessible publisher landing page. No TLS checks were disabled.
- Full affected `Maliev.Web.Tests` suite: 1190 passed, zero failures or skips, 9m58s.
- Final Release build: zero warnings and zero errors.
- Scoped `dotnet format whitespace --verify-no-changes --no-restore` passed for the five changed test files and `PublicSearchRouteCatalog.cs`.
- `git diff --check` and staged-file checks passed. English/Thai numerical parity in all three feeds-and-speeds worked examples passed.

The browser regression found that Chromium reports nonzero client rectangles for links inside closed `details`; focus filtering now uses actual visibility. The full suite also identified missing explicit route inventories, which were extended without weakening assertions.

Temporary logs, browser screenshots, link-audit receipts and test outputs are retained outside the repository under `D:/codex-temp/2026-10-14/task/knowledge-library-*`. Unrelated pre-existing images and verification drafts are preserved. Task-owned browser/server fixtures dispose their resources at test completion.

### Commands

```powershell
dotnet build Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-restore -m:1 /p:UseSharedCompilation=false /nodeReuse:false -warnaserror
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~KnowledgeCenterBrowserTests|FullyQualifiedName~KnowledgePageMigrationSourceTests|FullyQualifiedName~ApplicationAreaShellSourceTests|FullyQualifiedName~TechnicalSeoTests|FullyQualifiedName~SeoBusinessContractTests|FullyQualifiedName~ApplicationPageMigrationSourceTests'
dotnet test Maliev.Web.Tests/Maliev.Web.Tests.csproj -c Release --no-build --blame-hang-timeout 5m --blame-hang-dump-type none
node --check Maliev.Web/wwwroot/src/app/js/knowledge-navigation.js
node --test Maliev.Web.Tests/JavaScript/*.test.cjs
node --test Maliev.Web.Tests/Frontend/*.test.cjs
Invoke-Pester -Script Maliev.Web/tests/ProductionSeoVerifier.Tests.ps1 -PassThru
```

Native commands ran sequentially with the existing 4 GiB admission guard, two logical processors and 2 GiB managed heap cap. TRX results and build logs are retained outside the repository; the preview has a separate finite 45-minute lease.
