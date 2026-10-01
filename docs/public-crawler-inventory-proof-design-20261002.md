# Public crawler inventory — bounded test/design proof

## Root integration checkpoint

Root reviewed both frozen files and transferred only these two new paths into
the root-owned repricing workspace, based on accepted main18adfd9. Tracked child
issue453 will share PR452's combined acceptance lane as a separate coherent
commit; the source acce record is not automatically closed by this transfer.
No original file, existing test, runtime or provider was changed. A fresh combined
Release/focused/full/format/secret gate is required before committing this slice.
The agent's bounded diagnostic and test evidence below remains historical and
unaltered; its old browser-window limitation is not the new root full-suite gate.

Root combined acceptance: Release zero warnings/errors; 272 focused tests
passed, then the unfiltered Web suite passed 3,059/3,059 with zero failures,
errors or skips in 13m17s. TRX SHA-256
`CF46F80E31CC1A03DB4E57010B873C7291C639CADB2D59A0EA791FFB390298DB`.
Whole-solution format verification passed. Raw coverage remains unexcluded:
Web87.97%, Application81.74%, Infrastructure85.41%, Benchmark73.13%,
ServiceDefaults11.66%, CompatibilityContracts0%; these are distinct scopes,
not a claim that every referenced project reached 80%. Root independently read
the complete four-path source acce diff. Source closure still requires exact-head
and post-main acceptance and the explicit descendant-policy mapping below.

Status: test/design only; source `acce1386acf4d1525ee061cf9da77260525031f7` remains pending. No runtime repair, source-owner closure, ledger change, commit, push or GitHub mutation is authorized by this slice.

Base: `18adfd9b881a6b6d5756a1fdcb0dbdbda3ac79cb` (merged PR 449 main; post-main acceptance was pending when this slice began). Worktree: `B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002`, branch `codex/public-crawler-inventory-proof-20261002`. Only this document and `Legacy.Maliev.Web.Tests/PublicCrawlerInventoryAcceptanceTests.cs` are owned. Existing ignored dependencies and evidence remain intact.

## Frozen source and adaptations

Read-only committed mirror checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Source commit `acce1386acf4d1525ee061cf9da77260525031f7`, parent `24d001ad72e6ab3c45fe6684769b484859e2ed81`, subject **Add comprehensive SEO release contracts**, owns four paths:

- `Maliev.Web.Tests/SeoBusinessContractTests.cs`: literal 27-route inventory; physical route-to-page mapping; title/description/H1/indexability; ten commercial-intent pairs and specialist quotation links; three public layouts; robots/sitemap and secure owned origins.
- `Maliev.Web/Pages/Legal/Index.cshtml`: useful localized Legal description, not a submission handler.
- `Maliev.Web/tests/ProductionSeoVerifier.psm1`: actual public-document metadata contract and invocation for every public inventory route.
- `Maliev.Web/tests/ProductionSeoVerifier.Tests.ps1`: positive document fixtures, missing-description/noindex/duplicate-canonical/malformed-JSON controls and orchestration coverage. These are consumer contract inputs, not real rendered-page evidence by themselves.

The new tests independently freeze literals from those contracts; they do not call the target route catalog or document model to generate expected values. Public discovery metadata is now emitted through `Components/App.razor`, page `HeadContent`, `Components/Layout/PublicDocumentLinks.razor` and metadata components. Retained Razor main, instant-quotation and knowledge layouts are separately requested with their existing routing switches. Exact Razor source text is not the acceptance boundary.

Later committed source changes are additive/superseding context, not blanket retirement or automatic closure of those source records:

- `25db5545b0f5f575f61616af2ba54ee45e656a20`: custom-manufacturing Thai intent becomes `รับผลิตชิ้นงานตามแบบ`; the source test and actual latest source heading support the new phrase.
- `bf4c550cb28fd0432d9c178a7072d07d61e43e13`: script templates must not inflate title/heading counts. Test extraction removes scripts before document checks but separately parses original raw JSON-LD, decoding only individual HTML attributes.
- `7b1a8fd28c54f465c0ac966b3cf132ec49680bf7`: latest source requires recursively discoverable `Service` JSON-LD for specialist `/services/` pages.
- `e36435a51aa5471a19a790372fa6e43bc5f403c6` removes `/quotation` from the source sitemap catalog. `d6908049549f75b5e13c1858f4e5e2c2e0276752` removes it from the source verifier inventory and source business inventory. The historical route remains usable, with `noindex,follow`, not an intentionally failing indexability expectation. Both source records remain separate gates; GET does not prove their submission/business behavior.
- Latest source `bed10` has 26 indexed routes. Current target additionally publishes `/no-weapons`; two separately named additive cases verify it without substituting it for historical `/quotation` or claiming it belongs to acce.
- Accepted prior crawler/localization work uses resolved UI culture for English canonical URLs and query-before-cookie precedence. This is not a claim that later literal-query `4338bb31562ea3fdfe6fa7a8eb7952d0db961c68` is resolved. This slice uses explicit `culture=en` / `culture=th`, without broadening that separate compatibility gate.
- Later nationwide titles, service ownership, commercial wording, service coverage/shipping and measurement changes were inspected as descendants, but are not all accepted merely because these GET cases pass.

## Exact route/file matrix

Source paths below are relative to `Maliev.Web`; target page paths are relative to `Legacy.Maliev.Web/Components/Pages`. Every row has independent English and Thai HTTP cases.

| Route | Original source page | Current page |
| --- | --- | --- |
| `/` | `Pages/Index.cshtml` | `Home/HomePage.razor` |
| `/services` | `Pages/Services/Index.cshtml` | `Services/ServicesPage.razor` |
| `/services/custom-manufacturing` | `Pages/Services/Custom-Manufacturing.cshtml` | `Services/CustomManufacturingPage.razor` |
| `/services/3d-design` | `Pages/Services/3D-Design.cshtml` | `Services/ThreeDimensionalDesignPage.razor` |
| `/services/silicone-casting` | `Pages/Services/Silicone-Casting.cshtml` | `Services/SiliconeCastingPage.razor` |
| `/services/low-volume-injection-molding` | `Pages/Services/Low-Volume-Injection-Molding.cshtml` | `Services/LowVolumeInjectionMoldingPage.razor` |
| `/services/cnc-machining` | `Pages/Services/CNC-Machining.cshtml` | `Services/CncMachiningPage.razor` |
| `/services/3d-printing` | `Pages/Services/3D-Printing.cshtml` | `Services/ThreeDimensionalPrintingPage.razor` |
| `/services/3d-scanning` | `Pages/Services/3D-Scanning.cshtml` | `Services/ThreeDimensionalScanningPage.razor` |
| `/services/finishing-and-color` | `Pages/Services/Finishing-And-Color.cshtml` | `Services/FinishingAndColorPage.razor` |
| `/about` | `Pages/About/Index.cshtml` | `About/AboutPage.razor` |
| `/about/socialmedia` | `Pages/About/SocialMedia.cshtml` | `About/SocialMediaPage.razor` |
| `/contact` | `Pages/Contact/Index.cshtml` | `Contact/ContactPage.razor` |
| `/career` | `Pages/Career/Index.cshtml` | `Career/CareerIndexPage.razor` |
| `/quotation` | `Pages/Quotation/Index.cshtml` | `Quotation/QuotationPage.razor` |
| `/instantquotation/3d-printing` | `Pages/InstantQuotation/3D-Printing.cshtml` | `InstantQuotation/InstantQuotationPage.razor` |
| `/knowledges` | `Areas/Knowledges/Pages/Index.cshtml` | `Knowledges/KnowledgeIndexPage.razor` |
| `/knowledges/guidelines` | `Areas/Knowledges/Pages/Guidelines.cshtml` | `Knowledges/GuidelinesPage.razor` |
| `/knowledges/workflow` | `Areas/Knowledges/Pages/Workflow.cshtml` | `Knowledges/WorkflowPage.razor` |
| `/knowledges/specifications` | `Areas/Knowledges/Pages/Specifications/Index.cshtml` | `Knowledges/Specifications/SpecificationsIndexPage.razor` |
| `/knowledges/specifications/cnc-machining` | `Areas/Knowledges/Pages/Specifications/CNC-Machining.cshtml` | `Knowledges/Specifications/CncMachiningSpecificationPage.razor` |
| `/knowledges/specifications/3d-printing` | `Areas/Knowledges/Pages/Specifications/3D-Printing.cshtml` | `Knowledges/Specifications/ThreeDimensionalPrintingSpecificationPage.razor` |
| `/knowledges/specifications/3d-scanning` | `Areas/Knowledges/Pages/Specifications/3D-Scanning.cshtml` | `Knowledges/Specifications/ThreeDimensionalScanningSpecificationPage.razor` |
| `/legal` | `Pages/Legal/Index.cshtml` | `Legal/LegalPage.razor` |
| `/legal/privacypolicy` | `Pages/Legal/PrivacyPolicy.cshtml` | `Legal/PrivacyPolicyPage.razor` |
| `/legal/termsconditions` | `Pages/Legal/TermsConditions.cshtml` | `Legal/TermsConditionsPage.razor` |
| `/legal/nondisclosureagreement` | `Pages/Legal/NonDisclosureAgreement.cshtml` | `Legal/NonDisclosureAgreementPage.razor` |

## Observable contracts and deliberate proof limits

- 54 current-renderer cases: HTTP 200 and HTML content type; resolved `html lang`; one useful title; one description of at least 40 characters; one owned-origin canonical, English explicit culture / Thai bare path; exactly three stable English/Thai/default alternates; real nonempty H1 with source's multiple-heading exception only for instant quotation; useful Open Graph title/description; no index-blocking metadata or response header, except the explicit historical quotation utility adaptation.
- Raw JSON-LD is parsed rather than globally HTML-decoded. Non-knowledge routes require JSON-LD; knowledge routes may omit it but any published JSON-LD must parse. Latest-source specialist service pages require a `Service` node. These checks do not certify every business field, structured-data search eligibility or provider pricing.
- Six retained-renderer cases independently request `/`, `/instantquotation/3d-printing`, and `/knowledges/guidelines` through `_Layout.cshtml`, `_InstantQuotationLayout.cshtml`, and `_LayoutKnowledges.cshtml`. No fake public endpoint or direct component rendering replaces the HTTP pipeline.
- Twenty source-commercial-intent cases check language-specific phrases in the actual non-script rendered document (metadata and body) and actual owned quotation anchors for specialist routes. They do not require every phrase to be visible body text, validate complete body semantics or certify CNC capabilities/pricing. The failure they catch is removal of commercial discovery intent or the real quotation handoff, not cosmetic code rewriting.
- Two additive `/no-weapons` cases and four account/forgot-password controls ensure inventory additions do not weaken private or credential noindex policy.
- No Chromium, screenshots, accessibility engine, client interaction, upload, authoritative pricing, POST, consent/provider request, deployed production SEO, authentication producer, PostgreSQL read/write, joined Aspire or persisted order/quotation proof is performed. GET cannot prove model correctness or business submission acceptance. No production data or PII is used.

`PublicCrawlerInventoryFixture` inherits the unchanged `TestingWebApplicationFactory`. It controls external country and career reads using synthetic Thailand and an empty available career listing. Root additionally approved a throwing `IInstantQuotationSubmissionStore` external-storage boundary after the original Redis diagnostic: `TryAcquireAsync` increments its counter and throws; retained GET explicitly asserts zero calls. The actual submission service is not replaced. This proves the tested GET never touches checkpoints, not Redis correctness or submission workflow acceptance. The fixture does not inject an auth principal or alter permissions, route ownership, middleware, localization, CSRF or resources. Error diagnostics record exception category/method and allowlisted constructor/render messages only, not response bodies or request fields.

## Current evidence and prerequisite diagnostic

Both inspected private dependency heads match current CI pins: ServiceDefaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, CompatibilityContracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`. Build command:

`dotnet build Legacy.Maliev.Web.slnx -c Release --nologo -p:TreatWarningsAsErrors=true -p:UseSharedCompilation=false -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies`

Baseline and each test revision compiled with zero warnings and zero errors. Focus command:

`dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-member-auth-indexing-20261002/.dependencies --filter FullyQualifiedName~PublicCrawlerInventoryAcceptanceTests --logger 'trx;LogFileName=public-inventory-original.trx' --results-directory TestResults/public-inventory-20261002`

Original TRX is preserved unchanged: 86 total, 84 passed, 2 failed, zero skipped. All 54 current-renderer cases, 20 commercial cases, two additive and four utility controls passed. Four retained main/knowledge layout cases passed. Two retained instant-quotation GETs returned 500 before metadata assertions. A separate category-then-diagnosis rerun preserved the same 84/2 result.

Diagnosis: `InvalidOperationException` in `Microsoft.Extensions.DependencyInjection.ServiceLookup.CallSiteFactory.CreateArgumentCallSites`: unable to resolve `StackExchange.Redis.IConnectionMultiplexer` when activating `Legacy.Maliev.Web.Infrastructure.InstantQuotationSubmissionStore`. Testing deliberately configures distributed memory cache without a Redis multiplexer. The retained Razor `ThreeDimensionalPrinting` PageModel eagerly requests `IInstantQuotationSubmissionService`; its `OnGet` is empty, but constructor resolution reaches Redis-backed checkpoint storage. The current component GET does not eagerly resolve that submission service.

This was a fixture prerequisite failure, not a metadata-loss RED or a proven production failure. Root reviewed the actual constructor and storage interface and approved the new throwing-store boundary described above; no runtime repair, submission-service substitution or test skipping was performed. A fresh Release build remained zero warnings/errors, then the same 86 cases passed with zero failures/skips in `public-inventory-storage-boundary.trx`. All six retained layout cases reached real HTTP 200 and their complete metadata assertions; zero checkpoint calls were asserted. No genuine runtime RED was reproduced and no runtime change is justified by this result.

Evidence SHA-256: original `public-inventory-original.trx` = `964078B28420D184DDB4ACDC6FF872A16C885F0D716A31E570E6125281CC53A5`; first reviewed-storage GREEN `public-inventory-storage-boundary.trx` = `22EE01115900C196623C19E80DD30D9955BC1C9C139690E12A814EC0ED6379D2`. Additional diagnostic TRXs remain unchanged alongside them, not rewritten as green.

Full/unfiltered browser suite is deliberately not run while root owns issue 450's exclusive browser window. Source acce and all broader source/producer acceptance gates remain pending. Root review decides whether the narrower HTTP proof is sufficient for this source-owned behavior and what further non-GET/business/producer evidence is required; this lane does not declare source completion.

## Final bounded gates and handoff

- Final Release build, using the exact command above after the only new-file whitespace correction: zero warnings/errors.
- Final new-fixture run, same focused command with `LogFileName=public-inventory-final.trx`: 86 passed, zero failed/skipped. SHA-256 `57D6D91C0E46D7F3BD73D9528474EFDDCE2D6A36D7DD1053478A4D5EA1B8EE62`; parsed TRX counters independently agree with 86/86/0/0.
- Bounded affected non-browser group: same test command with filter `FullyQualifiedName~PublicDocumentLinksMigrationTests|FullyQualifiedName~PublicDocumentVerifierQuotationHttpTests|FullyQualifiedName~SeoBusinessContractParityTests|FullyQualifiedName~TechnicalSeoParityTests|FullyQualifiedName~SitemapResponseCachingHttpTests|FullyQualifiedName~AccountUtilityIndexingHttpTests`, and `LogFileName=public-inventory-affected-http.trx`: 100 passed, zero failed/skipped. SHA-256 `F96688DD0F06853F64012582C039EC30AB26F01B635280ED648F8DAA93ACF343`.
- `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore --include Legacy.Maliev.Web.Tests/PublicCrawlerInventoryAcceptanceTests.cs --verbosity minimal`, with process-local `MalievWorkspaceRoot` pointing to the owned `.dependencies`: final exit 0. The first check identified two whitespace issues in the new client-options initializer; only those new-file lines were corrected. No existing file was formatted.
- Tracked `git diff --exit-code`: exit 0. Working-tree inventory contains exactly the two new owned paths, with existing ignored dependencies/results retained.
- Final `git diff --check`: exit 0. Because the files are new/untracked, each also passed `git diff --no-index --check -- /dev/null <owned-file>` with no whitespace diagnostics; exit 1 is the normal new-file difference, not a whitespace failure. An initial wrapper incorrectly treated that exit 1 as failure; no product/file change was needed. A document structure check confirmed exactly 27 historical route mappings.
- Final two-owned-file `gitleaks stdin --redact --no-banner`: exit 0, no leaks found, without staging any file or printing secret values.

No whole Web suite or 80% project coverage claim is made from these focused GET cases. The browser window remains root-owned, and the full-suite/coverage follow-up must run in the serialized acceptance lane. No runtime repair is proposed: no genuine runtime RED was demonstrated. The result is passing existing-behavior proof plus preserved fixture diagnostics, ready for root's independent file/contract review and acceptance decision, not authorization to commit or close acce.
