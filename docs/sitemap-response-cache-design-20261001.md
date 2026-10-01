# Web434 sitemap public-cache header parity

Final candidate: the endpoint-local header repair, five new actual HTTP regression/control cases and this document. Earlier TEST/DESIGN statements below are retained as chronological evidence and superseded by the subsequently authorized repair/final validation sections. No other runtime, old tests, CI, inventory, global caching or deployment changes.

Owned worktree: `B:/maliev-legacy/.worktrees/web-sitemap-cache-20261001`, branch `codex/web-sitemap-cache-20261001`, exact protected base `e25168e76f829b147bb5ad415ffdbf570e19a574`. Canonical was clean and equal to live origin; main CI36827597546SUCCESS. All existing worktrees are preserved, including separately owned wall-browser and Quotation88 outputs. Only this new document and `Legacy.Maliev.Web.Tests/SitemapResponseCachingHttpTests.cs` are changed. No runtime/old tests/CI/source/ledger changes.

## Source and actual missing boundary

- `9f7a757a05bd158bb79e6c25fa444b9a4ed49cf8`, parent `bb092e0c49497873a7db67c9ce951095a211d778`: source `Maliev.Web/Pages/Sitemap.cshtml{,.cs}`, dynamic XML and `[ResponseCache(Duration=3600, Location=ResponseCacheLocation.Any)]`.
- `4d1aaf1a51b83e23e07fc5287b4549fa1e05d2bf`, parents `4e3aee5b6b2386e41e418d9c92ccf19340d165c7` and `9f7a757a05bd158bb79e6c25fa444b9a4ed49cf8`: sitemap merge record, not another independently missing feature.
- `023aa2f143fa5a8f557d72c1302b9add64792d6a`, parent `368af57f75c5be6166aeefca943d4ae7039d3b29`: actual sitemap delta adds the MVC import; separate breadcrumb and generated XML documentation are outside this slice.
- Later `92fe441270b7ad0fcbf89a6ad9633a07a318b3d9` changes search-route truthfulness. Final committed source at cutoff `bed10c7d15e0698e0b75f1329d0f312937f5d77f` still has the same3600public response-cache attribute with canonical inventory renderer. Do not restore old account aliases, quotation redirects or request-time fabricated lastmod.

Target `Legacy.Maliev.Web/SitemapEndpointRouteBuilderExtensions.cs` maps raw XML200 through `SitemapXmlRenderer.Render(PublicSearchRouteCatalog.Routes)`, excludes OpenAPI and preserves case-insensitive `/sitemap`; it supplies no Cache-Control. Program has output-cache services/middleware but this endpoint has no output-cache policy. Source ResponseCache is evidence of a response-header contract, NOT proof of server-side cached render hits; this proposal does not add storage, output-cache policy or a cache-hit claim.

Consumer boundary: unauthenticated crawlers/caches receive invariant UTF-8 XML for27 current canonical public routes with EN/TH/x-default alternate links. Private/account/member surfaces and redirect-only `/quotation` must not enter the inventory; caller culture/cookie input must not personalize the document or emit Set-Cookie. No service API/database/provider call is required for the sitemap.

## Genuine observed RED and controls

Private clean independent clones use existing CI pins: Defaults `6ea131df4bcf8d213d7d121cb8c865697bee7420`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; no shared assemblies/output directories. Every command uses `-p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-sitemap-cache-20261001/.dependencies`.

1. Untouched `dotnet build Legacy.Maliev.Web.slnx -c Release -p:UseSharedCompilation=false`:0warnings/0errors, `TestResults/sitemap-baseline-build.log`.
2. Existing focused HTTP/SEO controls:8PASS/0fail/0skip, `TestResults/sitemap-baseline-focus/natth_MALIEV-31USFIV_2026-10-01_17_44_36_net10.0.trx`.
3. New test-only Release build:0warnings/0errors, `TestResults/sitemap-red-build.log`.
4. Focus13:11PASS/2genuineRED/0skip, `TestResults/sitemap-red-focus/natth_MALIEV-31USFIV_2026-10-01_17_45_57_net10.0.trx`. Both EN and TH return actual200 and no Set-Cookie, then fail because response.Headers.CacheControl is NULL. Three new controls pass: literal27-route XML/order/three exact localized links/private exclusions/no fabricated lastmod for EN and TH; byte-identical XML across EN and TH with a synthetic unrelated caller-cookie marker and case variant `/Sitemap`. Eight existing tests remainPASS.

Tests use unchanged `TestingWebApplicationFactory`: actual Program/pipeline/endpoint/renderer in existing Testing environment, only the factory's existing synthetic reCAPTCHA configuration; no endpoint/authorization/service replacement. This is real in-process HTTP, not a mocked response, Production environment, browser, deployed edge/cache or live Search Console proof. Expectations use a literal inventory, not the production route catalog to generate expected output.

Focus command: `dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~SitemapResponseCachingHttpTests|FullyQualifiedName~Sitemap_|FullyQualifiedName~TechnicalSeoParityTests|FullyQualifiedName~SeoBusinessContractParityTests' --logger trx --results-directory TestResults/sitemap-red-focus` plus the absolute dependency property above. The diagnostic shell reads the log afterward; actual RED is established by parsed TRX/results, not that shell's final readback exit code.

## Proposed smallest runtime repair (not authorized or implemented)

Only the sitemap endpoint delegate receives HttpContext and sets `context.Response.Headers.CacheControl = "public,max-age=3600"` before returning its unchanged Results.Text. No global middleware/cache policy, query/culture/canonical logic, inventory, authentication, deployment or CI changes. Preserve no Set-Cookie and invariant body. Root must review/authorize first, then rerun Release0W0E, the genuine regressions and full affected suite/static checks before acceptance. Existing #29/#12 provide logical migration ownership; broader #22 content/schema enrichment is already accepted and not reopened by this narrow header defect.

The compiled tree is intentionally RED and not mergeable. Full suite/browser/assets/deployed edge checks are not claimed as passing or completed at this TEST/DESIGN stop; no runtime was changed and root review is required before repair. No GitHub mutation, commit/push, persistent data/schema, deployment, provider traffic, infrastructure or ledger resolution occurred.

Final new-file static checks: scoped `dotnet format Legacy.Maliev.Web.slnx --verify-no-changes --no-restore --include Legacy.Maliev.Web.Tests/SitemapResponseCachingHttpTests.cs` exit0; `git diff --check` exit0; separate redacted gitleaks scans on the new test and document exit0/no leaks. All handles terminal; private dependency pins rechecked clean. Root may inspect the frozen two-file TEST/DESIGN candidate; no runtime acceptance is claimed.

## Subsequently authorized endpoint-only repair

Root tracks this bounded repair as [Web434](https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Web/issues/434), child of the existing migration ownership. Concurrent Web433 work is root-coordinated and disjoint; this candidate remains exactly three files at its frozen base, without importing another writer's changes.

Root independently reproduced the two missing-header REDs after its own Release0W0E (focus13=11PASS/2RED/0skip, root-sitemap-red TRX17:49:11), then approved only the endpoint-local repair above. The delegate now receives HttpContext and sets `public,max-age=3600` before the unchanged XML Results.Text. No global output-cache policy, inventory, auth, middleware or CI change. Original2706 test cases remain untouched; five new cases make expected full2711. The endpoint has no application-level non-200 branch; adding a blanket error-cache policy or new error statuses is neither necessary nor proposed.

Fresh owned Release0W0E (`TestResults/sitemap-green-build.log`), actual normal HTTP focus13/13PASS/0skip (`TestResults/sitemap-green-focus/natth_MALIEV-31USFIV_2026-10-01_17_52_16_net10.0.trx`). The initial focused launch from the asset subdirectory failed log-path resolution before executing any test; it is not test evidence and was corrected by the successful root-directory invocation. Original RED TRX/source assertions remain unchanged.

Full-suite fixture preparation follows the existing committed CI scripts, using only owned ignored clones and outputs. `prepare-profile-producer-boundary.ps1` builds Customer `dc090542c02d675f54c3be59e33654dc24ecca9e` with Defaults8f4/Contracts78e, Auth `82c8d63dd08677a7f8ccd107c05dd6c9badbfd79` with Defaults5c5/Contracts78e, and both seed tools; four builds0W0E. Each clone is independent and exact-pinned, never a source/runtime edit. Chromium install/preparation and `verify-complete-source-history-parity.ps1 -ManifestOnly` exit0. These are disposable test dependencies, not new grants or activation. Asset/full terminal results remain pending in this chronology.

Whole-solution format verification exit0 (`TestResults/sitemap-whole-format.log`); four Web/Application/Infrastructure/Tests transitive .NET vulnerability audits exit0/no vulnerable packages; npm production audit exit0/0vulnerabilities; unchanged workflow actionlint exit0. Signing-resource scanner is loaded directly via `git show` from CI-pinned Workflows `d7efac266bc66273bc45eab583618871292ecbd6`, function `Test-JwtSigningResourceMaterial`, against tracked paths plus the two new files: PASS. Three owned-file redacted gitleaks scans PASS; final document rescan remains required after terminal evidence update.

`npm run ci` terminalexit0 (`TestResults/sitemap-assets.log`): browser modules176PASS/0fail/0skip; CNC575total/565PASS/0fail/10existingSKIP. Exact exclusions are six saved native interpretation cases lacking `CNC_NATIVE_INTERPRETATION_FIXTURES`, three saved native thread cases whose private evidence is not distributed (public thread coverage runs separately), and one native topology case with unmounted private original CAD corpus. No skipped condition was altered or overridden, and no private source/data was acquired for this header slice. Deterministic committed `wwwroot/dist` regeneration has no diff. Chromium preparation and manifest-only check passed; assets are not a deployed edge/output-cache-hit claim.

## Final full acceptance evidence

Full `.NET` suite executed2711/PASS2711/failed0/skipped0, commandexit0, duration10m24s: `TestResults/sitemap-final-full/natth_MALIEV-31USFIV_2026-10-01_17_58_01_net10.0.trx`, log `TestResults/sitemap-final-full.log`. All2706 original cases remain unchanged and passing plus five new HTTP cases. Full suite includes the repository's browser/disposable profile boundaries; it does not prove production Aspire/cutover, live provider behavior, deployed edge caching or server-side sitemap cache hits.

Exact full command: `dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/web-sitemap-cache-20261001/.dependencies --logger trx --results-directory TestResults/sitemap-final-full`. The four MALIEV_PROFILE_* binary environment variables point only at this worktree's privately pinned Customer/Auth and owned seed outputs produced by the unchanged preparation script. No shared service binaries are used. Build→focus completed before full; assets terminated before full and no .NET build/test/format overlapped another .NET output writer. Full npm audit including development dependencies alsoexit0/0vulnerabilities (`TestResults/sitemap-npm-full-audit.log`).

Root independent review/build/focus/full and protected CI remain required before integration. This narrow public-response header repair does not resolve all eight source ledger records, broader SEO/content issues or owner Aspire/deployment gates. No commits/pushes, GitHub writes, provider traffic, persistent migrations/data changes or source/original/canonical edits were made.

Final TRX was independently parsed:2711executed/2711PASS/0failed/0notExecuted, exactly five new class casesPASS; SHA256 `A07683DD3248986B864ADE2EC2399B67D46D0C8EDCE44DBC335D15AF16E4D1E1`. After full terminal, final whole-format verification exit0 (`TestResults/sitemap-final-format.log`), actionlint exit0 (`TestResults/sitemap-final-actionlint.log`), diff check exit0 and final document gitleaks exit0. All owned execution handles are terminal. Candidate frozen for root acceptance, exactly three files and unchanged private pins/committed assets.

## Independent root gate

Root initially supplied a nonexistent local dependency path and the build failed2errors before runtime validation; corrected actual private `.dependencies` produced Release0warnings/0errors and focus13PASS0skip. Three scoped scans/actionlint/diffcheck passed. Independent full then executed2711:2710PASS/oneFAIL/zeroSKIP, the unchanged Thai1280light ReviewLayout browser Goto load timed out. A TRX file was not found in the expected results directory on follow-up read; the tool's failure/total summary is retained, not a claimed artifact hash. Concurrentpaymentfull also had one unchanged browser timeout. Both full runs are terminal. Resource pressure is a hypothesis, not a waiver. No timeout, assertion, skip or existing browser-test change was made.

## Root serial acceptance after current-main integration

Root rebased the three owned files onto protected main `c0e63e2c51865433703ec551228b806443b93ba3`, preserving all new tests and unrelated work. The newly accepted SCB boundary adds24 existing cases to this base; this sitemap slice still adds only5. Independently prepared private profile and SCB helper builds and the affected Web Release build each finished with0warnings/0errors. New HTTP focus5PASS/0skip. Exclusive serial full suite:2735executed/2735PASS/0failed/0skipped in11m3s, terminalexit0. TRX `TestResults/root-sitemap434-serial-full/root-sitemap434-serial-full.trx`, SHA256 `666ACC159D100A68F6AA36603515C7117FBE4EFFEF819E36AC5644852FFB2103`.

Root whole-solution format verification, all five dependency audits, actionlint and diff checks passed on this integrated candidate. Earlier failed evidence remains recorded; the serial result supersedes it for acceptance without changing any browser timeout/assertion/skip. No output-cache hit, deployed edge behavior, complete source-owner resolution, production data parity or Aspire acceptance is claimed. Protected head and post-merge CI remain required before closure.

## Replacement-head integration with accepted culture canonicalization

After Web438 merged, root rebased this same three-file slice onto protected main
`33eff14e8e1aa88c7833507add00f7032cb96cf2`; resulting runtime/test commit is
`86c31d2b08a8713f3881f4ce0f80848907f3c3af`. The old PR-head CI result is not
acceptance of this replacement. Independent private profile/SCB preparation and Web
Release build completed with zero warnings/errors. HTTP focus37 passed without
skips. Exclusive serial full suite executed2767/PASS2767/failed0/skipped0 in10m13s,
terminal exit0. TRX `TestResults/root-sitemap434-cookie-integration-full/integrated-full.trx`,
SHA256 `3C05D4B78DC9D8BDAB30C1B791E196A46A2CA650AE91F4680930E65200780331`.
The accepted culture changes contribute32 cases; this slice still contributes5.
All earlier failure evidence is retained. No server-cache-hit, deployed edge,
Aspire, complete source-owner or production-data parity claim follows from this run.
Replacement-head CI and subsequent exact-main CI remain required.
