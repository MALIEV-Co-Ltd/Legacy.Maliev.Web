# Complete Maliev.Web source-history parity through `370fe20`

## Scope and result

This is the full-history companion to `docs/source-parity-through-370fe20.md`.
It inventories every commit reachable from source `main` that touches
`Maliev.Web` or `Maliev.Web.Tests`, from the initial commit through
`370fe2010b9b63646151222fac3959eebed85dc4`.

- Source commits affecting Web or Web tests: **298**.
- Pre-extraction through `a40ae59`: **105**.
- After extraction through `dcc088f`: **123**.
- After `dcc088f` through the source head: **70**.
- The nine repository-only commits in the later 79-commit delta remain recorded
  in the companion ledger and are deliberately not counted as Maliev.Web commits.
- Source repository: read-only. Production deployment: not authorized.

Disposition totals in this inventory:

- Migrated: **251**
- Validation translated: **16**
- No unique change: **3**
- Superseded safely: **6**
- Excluded tooling: **4**
- Release gate: **18**

“Superseded safely” is used only when later source history intentionally replaced
the behavior, such as checked-in credentials, temporary LINE outage UI, or the
owner-rejected IBM font choice. “Release gate” means the Web-side contract is
translated, but proof belongs to GitOps, Workload Identity, another Legacy
service, or owner Aspire acceptance.

## Evidence families

| Key | Current Legacy evidence |
| --- | --- |
| Foundation | .NET 10 Blazor SSR/BFF host, route catalog, security defaults, health checks, and full Release build. |
| Public UI | Route-specific SSR tests, `WebSurfaceTests`, localized resources, responsive/source parity tests, deterministic assets, and browser modules. |
| Account | Account/member route, antiforgery, cookie/session, Auth client, credential recovery, first-login, and AuthService integration tests. |
| Quotation | Pricing, upload, geometry, multi-part, persistence/finalization, member/manual quotation, and browser module suites. |
| Search/measurement | SEO, sitemap/canonical/structured-data, CSP, Consent Mode, GTM, persisted conversion, contact-channel, robots, and PII-negative tests. |
| Delivery | GitOps and Aspire gates; deployment scripts and embedded secrets are never copied into Web source. |
| Quality | Equivalent .NET 10 architecture tests and CI checks; obsolete Razor/jQuery source-shape tests are translated rather than copied verbatim. |

## Complete per-commit inventory

| Source commit | Date | Disposition | Evidence family | Source intent |
| --- | --- | --- | --- | --- |
| `5fac706` | 2025-07-18 | Migrated | Foundation | Initial commit |
| `3a39321` | 2025-08-23 | Release gate | Delivery | separate deployment and sevice ingress |
| `0822636` | 2025-10-23 | Release gate | Delivery | Add resource limits and remove nodeSelector constraints |
| `3a10450` | 2025-10-23 | Release gate | Delivery | Remove all nodeSelector constraints from deployment files |
| `bb092e0` | 2025-10-27 | Release gate | Foundation | Fix critical resource constraints causing 502 errors |
| `3f8a0e6` | 2025-10-27 | Migrated | Search/measurement | Add comprehensive structured data (Schema.org) markup |
| `9f7a757` | 2025-10-27 | Migrated | Search/measurement | Add dynamic XML sitemap for improved SEO indexation |
| `4d1aaf1` | 2025-10-27 | No unique change | Search/measurement | Merge pull request #2 from MALIEV-Co-Ltd/feature/xml-sitemap |
| `d9ab21a` | 2025-10-27 | Migrated | Search/measurement | Optimize for local SEO: Add Bangkok/Nonthaburi location keywords |
| `264ae97` | 2025-10-27 | No unique change | Search/measurement | Merge pull request #3 from MALIEV-Co-Ltd/feature/local-seo-optimization |
| `9670830` | 2025-10-27 | Migrated | Foundation | fix blank text on english page |
| `368af57` | 2025-10-31 | Release gate | Foundation | Fix 502 errors: Improve health probes and resource allocation |
| `023aa2f` | 2025-10-31 | Validation translated | Search/measurement | Update XML documentation and SEO schema files |
| `72eb9f1` | 2025-11-12 | Migrated | Foundation | update |
| `a67b608` | 2025-11-12 | Superseded safely | Account | store recaptcha directly in appsettings.json |
| `5458b7d` | 2025-11-18 | Release gate | Delivery | Remove Maliev.Web.BlazorServer project and update deployment configurations |
| `53f4baf` | 2025-11-20 | Release gate | Delivery | Add resource limits and requests to all deployment manifests |
| `93f9f99` | 2025-11-23 | Release gate | Delivery | perf: optimize resource limits in deployment.yaml files |
| `91c1623` | 2026-07-10 | Superseded safely | Foundation | feat(web): add LINE OA recovery alert |
| `3e30796` | 2026-07-10 | Validation translated | Quality | test(web): strengthen LINE OA alert coverage |
| `550eaea` | 2026-07-10 | Migrated | Foundation | fix(web): use local data-protection keys in development |
| `90f34b3` | 2026-07-10 | Release gate | Delivery | split the frontend deployment scripts |
| `acbe1b2` | 2026-07-10 | Migrated | Quotation | improve the instant quotation |
| `0ae8018` | 2026-07-11 | Migrated | Quotation | feat(web): improve instant-quotation FDM pricing accuracy |
| `6e66b40` | 2026-07-11 | Migrated | Foundation | fix(build): resolve CS1998 async-without-await warnings |
| `5b7d303` | 2026-07-12 | Migrated | Public UI | feat(web): redesign localized landing and shared shell |
| `e7588d3` | 2026-07-12 | Validation translated | Public UI | test(web): align font guard with shared shell |
| `f72adf4` | 2026-07-12 | Migrated | Quotation | fix(web): reconcile instant quotation pricing |
| `b8bc150` | 2026-07-12 | Migrated | Quotation | fix(web): align instant quotation with reference |
| `fb549ad` | 2026-07-12 | Superseded safely | Foundation | feat(web): improve LINE outage notice |
| `5c4e1ef` | 2026-07-13 | Migrated | Public UI | fix(web): refine landing hero and mobile layout |
| `21f781d` | 2026-07-13 | Migrated | Public UI | fix(web): make landing hero media full bleed |
| `0052e50` | 2026-07-13 | Migrated | Quotation | fix(web): localize quotation review summary |
| `027e2d6` | 2026-07-13 | Migrated | Public UI | fix(web): align landing collage masks and focus |
| `bb47720` | 2026-07-13 | Migrated | Public UI | feat(web): rebuild manufacturing service guides |
| `6948e97` | 2026-07-13 | Migrated | Public UI | fix(web): position landing hero focal subjects |
| `35ee905` | 2026-07-13 | Migrated | Public UI | fix(web): fill and offset landing collage |
| `0e85cee` | 2026-07-13 | Migrated | Public UI | fix(web): fill landing collage leading mask |
| `62e03f7` | 2026-07-13 | Migrated | Public UI | style(web): widen primary navigation spacing |
| `add7728` | 2026-07-13 | Validation translated | Public UI | test(web): reconcile landing collage contract |
| `6ade732` | 2026-07-13 | Migrated | Foundation | style(web): align legacy page typography |
| `86ab8f9` | 2026-07-13 | Migrated | Public UI | style(web): standardize supported font weights |
| `a14334e` | 2026-07-13 | Validation translated | Public UI | test(web): scope authored font weight guard |
| `96c9fc4` | 2026-07-13 | Migrated | Public UI | fix(web): align responsive hero diagonal geometry |
| `9ac0aa6` | 2026-07-13 | Migrated | Public UI | style(web): render hero dividers on white |
| `e701568` | 2026-07-13 | Migrated | Public UI | fix(web): expose service imagery across layouts |
| `13771af` | 2026-07-13 | Migrated | Quotation | content(web): expand service pricing FAQs |
| `8a2c1d3` | 2026-07-13 | Migrated | Public UI | fix(web): preserve service image compositions |
| `1969907` | 2026-07-13 | Migrated | Public UI | feat(web): add material comparison |
| `534309b` | 2026-07-13 | Migrated | Public UI | feat(web): redesign public inquiry pages |
| `4199bf3` | 2026-07-13 | Migrated | Quotation | fix(web): streamline quotation review flow |
| `a30dd82` | 2026-07-13 | Migrated | Foundation | feat(web): add shared application design foundation |
| `64fd32a` | 2026-07-13 | Migrated | Account | feat(web): modernize account and legal pages |
| `d05c700` | 2026-07-13 | Migrated | Account | feat(web): add responsive member and knowledge shells |
| `9a6f342` | 2026-07-13 | Migrated | Account | feat(web): migrate member workspace pages |
| `7d42b06` | 2026-07-13 | Migrated | Public UI | feat(web): refresh localized knowledge center |
| `8da7383` | 2026-07-13 | Migrated | Account | fix(web): harden member workspace responsiveness |
| `b4ef694` | 2026-07-13 | Migrated | Foundation | feat(web): complete public page migration |
| `b1aafb6` | 2026-07-13 | Migrated | Search/measurement | feat(web): redesign privacy consent notice |
| `032d958` | 2026-07-13 | Migrated | Quotation | fix(web): persist instant quotation submissions |
| `a300df9` | 2026-07-13 | Migrated | Delivery | fix(web): harden deployed UI and form workflows |
| `c08327b` | 2026-07-13 | Migrated | Public UI | feat(web): add service card photography |
| `ed21a9e` | 2026-07-13 | Migrated | Public UI | style(web): remove landing hero divider effects |
| `6c6cc1f` | 2026-07-13 | Migrated | Foundation | style(web): use bootstrap language select |
| `eea3b0b` | 2026-07-13 | Migrated | Foundation | fix(web): update Thai navbar labels |
| `1366ad2` | 2026-07-13 | Migrated | Public UI | style(web): normalize service eyebrow weight |
| `496dcf4` | 2026-07-13 | Migrated | Quotation | fix(web): route manual quotes to public RFQ |
| `4470620` | 2026-07-13 | Migrated | Account | feat(web): reuse customer profiles in inquiry forms |
| `3879b78` | 2026-07-13 | Migrated | Foundation | chore(web): remove resolved LINE outage notice |
| `e27008a` | 2026-07-13 | Release gate | Delivery | fix(web): enforce canonical HTTPS at GKE edge |
| `f0b3422` | 2026-07-13 | Validation translated | Quality | test(web): harden trusted-edge security contracts |
| `4b305af` | 2026-07-13 | Migrated | Search/measurement | fix(web): protect caching and Meta consent |
| `92fe441` | 2026-07-13 | Migrated | Foundation | fix(web): make search routes and lead failures truthful |
| `e1f35fb` | 2026-07-13 | Migrated | Search/measurement | fix(web): contain lead analytics queue failures |
| `541e0d2` | 2026-07-13 | Migrated | Search/measurement | feat(web): add privacy-safe lead and channel measurement |
| `c5995b4` | 2026-07-13 | Migrated | Search/measurement | feat(web): expose canonical Knowledge documents |
| `55413d7` | 2026-07-13 | Validation translated | Quality | docs(web): refresh generated API documentation |
| `4c4ba60` | 2026-07-13 | Validation translated | Quality | chore(web): normalize changed source formatting |
| `1b1d223` | 2026-07-13 | Validation translated | Search/measurement | test(web): add production SEO release verifier |
| `2f0dbfe` | 2026-07-14 | Migrated | Public UI | feat(web): add custom manufacturing intent page |
| `5fb68a9` | 2026-07-14 | Migrated | Foundation | fix(web): align public business hours |
| `59e1326` | 2026-07-14 | Validation translated | Public UI | test(web): classify custom manufacturing page |
| `d69a2c0` | 2026-07-14 | Migrated | Search/measurement | feat(seo): sharpen Thai service intent |
| `4104d67` | 2026-07-14 | Migrated | Search/measurement | feat(seo): connect services to technical guides |
| `407e8bd` | 2026-07-14 | Migrated | Search/measurement | fix(seo): publish stable localized alternates |
| `2c6ed6d` | 2026-07-14 | Migrated | Search/measurement | feat(seo): add localized sitemap alternates |
| `f0ac462` | 2026-07-14 | Migrated | Public UI | perf(web): prioritize service hero images |
| `e4d1cc2` | 2026-07-14 | Validation translated | Quality | style(web): satisfy focused format gate |
| `dd9de30` | 2026-07-14 | Migrated | Search/measurement | feat(seo): add weekly query cohort reporter |
| `0922247` | 2026-07-14 | Migrated | Search/measurement | fix(seo): qualify scanning accuracy claims |
| `ee9dc5c` | 2026-07-14 | Migrated | Search/measurement | feat(seo): identify verified social profiles |
| `77e9e6a` | 2026-07-14 | Migrated | Search/measurement | feat(seo): expose verified social profiles |
| `d6a4269` | 2026-07-14 | Migrated | Search/measurement | fix(a11y): name social profile links |
| `e35ea63` | 2026-07-14 | Migrated | Search/measurement | feat(seo): add localized service breadcrumbs |
| `c909e0e` | 2026-07-14 | Migrated | Foundation | fix(web): expose Razor measurement boundary |
| `770db9a` | 2026-07-14 | Migrated | Search/measurement | feat(seo): expose verified service location |
| `1531869` | 2026-07-14 | Migrated | Search/measurement | fix(seo): align local entity map coordinates |
| `37d9b8f` | 2026-07-14 | Migrated | Foundation | feat(web): request honest post-shipment reviews |
| `8f56fe5` | 2026-07-14 | Migrated | Search/measurement | feat(analytics): measure Google review link use |
| `0bf6df2` | 2026-07-14 | Migrated | Account | fix(seo): noindex public account utilities |
| `a3fdc43` | 2026-07-14 | Migrated | Search/measurement | fix(seo): align entity claims with verified services |
| `9362b24` | 2026-07-14 | Migrated | Public UI | perf(web): render icons without Font Awesome runtime |
| `936e963` | 2026-07-14 | Migrated | Search/measurement | fix(seo): make production verifier follow redirect contract |
| `70894c7` | 2026-07-14 | Migrated | Search/measurement | fix(web): refine consent and service CTA contrast |
| `a40ae59` | 2026-07-15 | Migrated | Account | security(web): remove embedded credentials and TLS bypass |
| `b6c8c6a` | 2026-07-18 | Migrated | Account | security: externalize web Google credentials |
| `79fa2e5` | 2026-07-18 | Migrated | Account | security: validate reCAPTCHA options at startup |
| `cee3801` | 2026-07-21 | Migrated | Quotation | fix(web): back instant-quotation session id with a signed cookie |
| `9ac4271` | 2026-07-21 | Migrated | Quotation | feat(web): return JSON from instant quotation submit handler |
| `2184a4d` | 2026-07-21 | Migrated | Quotation | feat(web): submit instant quotation via AJAX instead of a page post |
| `d5d67b6` | 2026-07-21 | Migrated | Quality | chore(web): regenerate XML doc comments for new submission helpers |
| `a649db9` | 2026-07-21 | No unique change | Foundation | Merge remote-tracking branch 'origin/main' |
| `3f5c830` | 2026-07-21 | Migrated | Quotation | feat(web): add background worker for instant-quotation model parsing |
| `ae2698d` | 2026-07-21 | Migrated | Quotation | fix(web): exclude worker from gulp minification bundle |
| `b6bbde0` | 2026-07-21 | Migrated | Quotation | feat(web): parse instant-quotation models via the background worker |
| `cd3110c` | 2026-07-21 | Migrated | Foundation | fix(web): guard the GLB/GLTF synchronous parse call with try/catch |
| `e0ea4fc` | 2026-07-21 | Migrated | Quotation | feat(web): reject oversized instant-quotation uploads client-side |
| `d2bae09` | 2026-07-21 | Migrated | Quotation | fix(web): wire worker cancellation, cap respawn loop, fix GLB shear |
| `5b6d94a` | 2026-07-21 | Superseded safely | Foundation | add the missing api key and cleanup lineoa |
| `03eaff1` | 2026-07-21 | Migrated | Delivery | chore: add .dockerignore files for service Docker builds |
| `e6f1f98` | 2026-07-21 | Migrated | Search/measurement | Fix LINE contact classification and align GTM event tests |
| `34b401e` | 2026-07-21 | Release gate | Delivery | fix(web): gate RazorRuntimeCompilation on Development, raise startupProbe timeout |
| `7fbd46e` | 2026-07-21 | Release gate | Delivery | fix(web): raise startupProbe timeout to absorb cold-start warmup |
| `61b73e2` | 2026-07-21 | Superseded safely | Foundation | fix(web): restore connection strings to appsettings.json |
| `7fe4bc7` | 2026-07-21 | Migrated | Quotation | fix(model-viewer): yield after worker parse to prevent page unresponsive on large CAD files |
| `7f24b24` | 2026-07-21 | Migrated | Public UI | fix(3d-printing): prevent page unresponsive, disable form fields during submit, fix Thai text encoding |
| `6268fb4` | 2026-07-22 | Migrated | Delivery | fix(web): include built static assets in container context |
| `f68eb36` | 2026-07-22 | Migrated | Foundation | fix(web): remove deprecated Facebook SDK widgets |
| `8f1a75c` | 2026-07-22 | Migrated | Foundation | feat(web): emit persisted lead conversion events |
| `4338bb3` | 2026-07-22 | Migrated | Search/measurement | fix(web): keep canonical tags self-referencing |
| `8447c95` | 2026-07-22 | Migrated | Account | fix(web): consolidate member crawl directives |
| `b673b36` | 2026-07-22 | Migrated | Foundation | fix(web): align upload limit with edge ceiling |
| `f99706d` | 2026-07-22 | Migrated | Foundation | feat(web): add baseline browser security headers |
| `2c18bda` | 2026-07-22 | Release gate | Delivery | fix(web): deploy backend security policy |
| `94886e4` | 2026-07-22 | Migrated | Quotation | feat(web): expose manufacturing services and localize quotation completion |
| `f6f0154` | 2026-07-22 | Migrated | Public UI | Add low-volume injection molding service guide |
| `e3595fc` | 2026-07-22 | Migrated | Search/measurement | fix(web): allow Google Ads conversion collection |
| `b9b57c6` | 2026-07-22 | Migrated | Public UI | feat(web): expand service discovery and visual guides |
| `b67a65d` | 2026-07-22 | Migrated | Public UI | feat(web): refine service layouts and process guidance |
| `8813dbb` | 2026-07-22 | Migrated | Public UI | fix(web): bound service card cover heights |
| `26490ca` | 2026-07-22 | Migrated | Public UI | fix(web): add service breadcrumbs and horizontal injection hero |
| `c6bcd27` | 2026-07-22 | Validation translated | Foundation | feat(web): add PRODUCT.md, DESIGN.md, and live-mode config |
| `95363e2` | 2026-07-22 | Migrated | Public UI | fix(web): show full injection hero and card asset |
| `7453ce0` | 2026-07-22 | Migrated | Public UI | fix(web): refine responsive service presentation |
| `d2560ee` | 2026-07-22 | Validation translated | Public UI | docs(web): explain why POM is excluded from low-volume molding |
| `c9ad216` | 2026-07-22 | Migrated | Public UI | feat(web): use wide injection hero and shared quick facts |
| `16ecc8c` | 2026-07-22 | Migrated | Foundation | fix(web): version compressed production stylesheets |
| `b9f8b3e` | 2026-07-22 | Migrated | Public UI | fix(web): refresh injection card and service transition |
| `c8b9f6a` | 2026-07-22 | Migrated | Public UI | fix(web): use wide injection hero and restore overlap |
| `4310698` | 2026-07-22 | Migrated | Public UI | fix(web): fill injection hero and lock quick-facts overlap |
| `d52450b` | 2026-07-22 | Migrated | Public UI | fix(web): visibly bridge service quick facts |
| `8b787a1` | 2026-07-22 | Migrated | Public UI | feat(web): add interactive service finder |
| `c7ca347` | 2026-07-22 | Migrated | Public UI | fix(web): wire service finder results to its controller |
| `4882ba6` | 2026-07-22 | Migrated | Search/measurement | fix(web): allow analytics conversion beacons through CSP |
| `bf59079` | 2026-07-22 | Migrated | Public UI | fix(web): restore service finder contrast and sizing |
| `62355c0` | 2026-07-22 | Migrated | Public UI | feat(web): add chained service finder recommendations |
| `43ec8ae` | 2026-07-22 | Migrated | Public UI | fix(web): close service finder results spacing |
| `9b8146c` | 2026-07-22 | Migrated | Public UI | feat(web): track persisted service finder journeys |
| `518af27` | 2026-07-22 | Validation translated | Quality | test(web): cover intranet finder summary boundary |
| `2d4fa4a` | 2026-07-22 | Migrated | Account | fix(web): validate recaptcha forms before submit |
| `c08f535` | 2026-07-22 | Migrated | Public UI | feat(web): add round MALIEV favicon |
| `f88e09b` | 2026-07-22 | Migrated | Public UI | Fill homepage services grid and update Thai project copy |
| `4ceb353` | 2026-07-23 | Migrated | Public UI | Fix homepage services directory link |
| `ae75d01` | 2026-07-23 | Migrated | Foundation | Localize validation messages across web forms |
| `6ced810` | 2026-07-23 | Migrated | Foundation | Remove implicit language selector validation |
| `80ef181` | 2026-07-23 | Migrated | Search/measurement | Redesign cookie consent banner for light mode |
| `ca46324` | 2026-07-23 | Migrated | Public UI | Add 3D printing process and file visuals |
| `b7539ba` | 2026-07-23 | Validation translated | Delivery | chore(repo): track deployment and XML documentation updates |
| `4932c8f` | 2026-07-23 | Migrated | Search/measurement | fix(seo): align English cookie canonical URLs |
| `3e59654` | 2026-07-23 | Validation translated | Public UI | test(web): align service image and font policies |
| `ea7adef` | 2026-07-23 | Migrated | Public UI | fix(web): refresh favicon assets from MALIEV mark |
| `0093f96` | 2026-07-23 | Migrated | Quotation | feat(web): refine instant quotation workspace |
| `35319bd` | 2026-07-23 | Migrated | Quotation | fix(web): stabilize quotation metrics on narrow panes |
| `9778a60` | 2026-07-23 | Migrated | Quotation | fix(web): keep quotation thumbnails square |
| `2ba62d4` | 2026-07-23 | Migrated | Quotation | fix(web): preserve quotation submission state across release |
| `26b7f33` | 2026-07-23 | Migrated | Quotation | fix(web): refine instant quotation review UI |
| `88a7b9c` | 2026-07-23 | Migrated | Quotation | fix(web): remove quotation preview pane inset |
| `0afa6ad` | 2026-07-23 | Migrated | Public UI | feat(web): add service directory preview card image |
| `4766110` | 2026-07-23 | Migrated | Public UI | feat(web): contextualize service finder options |
| `2b35ab9` | 2026-07-23 | Migrated | Public UI | feat(web): add adaptive service finder refinements |
| `b8d5d8e` | 2026-07-23 | Migrated | Public UI | feat(web): clarify 3d scanning deliverables |
| `89e7080` | 2026-07-23 | Migrated | Public UI | feat(web): add onsite scanning checklist |
| `fb4848a` | 2026-07-23 | Migrated | Public UI | feat(web): replace service directory image with responsive preview |
| `4ce198c` | 2026-07-23 | Migrated | Public UI | fix(web): make homepage CTA responsive |
| `218cc5b` | 2026-07-23 | Migrated | Public UI | feat(web): redesign 3d scanning workflow |
| `2b21aa5` | 2026-07-23 | Migrated | Public UI | fix(web): keep scanning delivery path on one line |
| `d0a82ad` | 2026-07-23 | Migrated | Public UI | fix(web): update service contact localization |
| `b01c219` | 2026-07-23 | Migrated | Public UI | fix(web): clarify service finder guide label |
| `1cb7526` | 2026-07-23 | Migrated | Public UI | fix(web): align service finder result answers |
| `c2fd8ac` | 2026-07-23 | Migrated | Public UI | fix(web): clarify optional service finder steps |
| `87370c2` | 2026-07-23 | Migrated | Public UI | feat(web): tailor service finder by manufacturing process |
| `8a0c6fb` | 2026-07-23 | Migrated | Foundation | fix(web): standardize Thai resin spelling |
| `9a3fc5e` | 2026-07-23 | Migrated | Public UI | feat(web): animate scanning workflow stages |
| `1583f4b` | 2026-07-23 | Migrated | Public UI | fix(web): connect landing service progress rail |
| `36b4ef4` | 2026-07-23 | Migrated | Public UI | fix(web): stack service directory card on mobile |
| `937b531` | 2026-07-23 | Migrated | Public UI | fix(web): center landing CTA sample part |
| `7e3847e` | 2026-07-24 | Migrated | Search/measurement | fix(web): address OpenSEO crawl and metadata issues |
| `5b6fe64` | 2026-07-24 | Migrated | Public UI | fix(web): align authored font weights with supported range |
| `c061b89` | 2026-07-24 | Migrated | Account | fix(seo): prevent cloudflare email links from breaking |
| `85695f8` | 2026-07-24 | Migrated | Public UI | fix(web): ensure scanning workflow nodes stay circular and readable |
| `4f582d7` | 2026-07-24 | Migrated | Public UI | fix(web): use horizontal service cards on mobile |
| `3a2fd36` | 2026-07-24 | Migrated | Public UI | feat(web): clarify 3d printing finishing scope |
| `e06dedb` | 2026-07-24 | Migrated | Public UI | fix(web): clarify 3d printing finish requirements |
| `c96f3c5` | 2026-07-24 | Migrated | Public UI | fix(web): localize legal pages and improve career mobile layout |
| `fd3e9a9` | 2026-07-24 | Migrated | Public UI | feat(web): add service finder CTA and page navigation |
| `e816a7f` | 2026-07-24 | Migrated | Public UI | fix(web): restore sticky service TOC and prevent clipping |
| `91a8f12` | 2026-07-25 | Migrated | Public UI | fix(web): hide mobile service TOC scrollbar |
| `72163e9` | 2026-07-25 | Release gate | Delivery | fix: propagate deployment script status reliably |
| `2d291c2` | 2026-07-25 | Migrated | Search/measurement | feat(web): add measured WhatsApp contact channel |
| `de499ad` | 2026-07-25 | Migrated | Quotation | Improve inquiry and service pricing UX |
| `c766526` | 2026-07-25 | Migrated | Public UI | Add expandable 3D printing material details |
| `80b69eb` | 2026-07-25 | Migrated | Public UI | Verify material colour availability and cache catalogue |
| `19de2a4` | 2026-07-25 | Migrated | Public UI | Correct material catalogue source references |
| `19848c4` | 2026-07-25 | Migrated | Public UI | Add brand icons to service contact actions |
| `b58c201` | 2026-07-25 | Migrated | Quotation | Allow Instant Quotation WebAssembly under CSP |
| `9488463` | 2026-07-25 | Migrated | Quotation | Fix instant quotation viewer panel spacing |
| `21d8f45` | 2026-07-25 | Migrated | Search/measurement | Allow Google remarketing beacon images in CSP |
| `a79f42d` | 2026-07-25 | Migrated | Search/measurement | Align contact channel panel responsively |
| `b861ee6` | 2026-07-25 | Migrated | Foundation | Remove duplicate metric card right margin |
| `eaffed2` | 2026-07-25 | Migrated | Delivery | Remove PayPal, harden web deploy, externalize PdfService log connection |
| `d581768` | 2026-07-25 | Migrated | Foundation | Assert the security header middleware writes the policy exactly once |
| `601a025` | 2026-07-25 | Migrated | Quality | Regenerate Maliev.Web.xml to match committed doc comments |
| `bd7d0c3` | 2026-07-25 | Migrated | Quotation | Fix instant quote CAD worker CSP |
| `6f9d786` | 2026-07-25 | Migrated | Quotation | Improve instant quote form accessibility |
| `c050c0f` | 2026-07-26 | Migrated | Quotation | Add privacy-safe quote funnel diagnostics |
| `4a5d21a` | 2026-07-26 | Migrated | Quotation | Add quotation decision measurement |
| `932a7bf` | 2026-07-26 | Migrated | Public UI | fix(web): refresh favicon assets |
| `dcc088f` | 2026-07-26 | Migrated | Public UI | fix(web): version favicon links |
| `cc1deea` | 2026-07-27 | Migrated | Quotation | fix(quotation): reconcile per-part pricing and review selection |
| `3c0bdc3` | 2026-07-27 | Migrated | Quotation | fix(quotation): clarify review pricing and diagnostics |
| `e7a9521` | 2026-07-27 | Migrated | Quotation | fix(quotation): make instant quote layout responsive |
| `87f86bf` | 2026-07-27 | Migrated | Quotation | fix(quotation): restore review navigation locale binding |
| `3c967db` | 2026-07-27 | Migrated | Quotation | fix(quotation): keep 3mf parsing on main thread |
| `de4f9a9` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): keep metrics beside summary |
| `279660a` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): handle external 3mf components and wrap summary |
| `238ce8a` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): keep viewer layout responsive |
| `1fbb19b` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): align configurator pricing summary |
| `edf451e` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): light 3mf preview geometry |
| `d88d279` | 2026-07-27 | Migrated | Public UI | fix(web): align material details and active service TOC |
| `03a7736` | 2026-07-27 | Migrated | Quotation | fix(instant-quotation): shade 3mf flat, unstack laptop layout, meet AA |
| `f701209` | 2026-07-27 | Migrated | Quotation | feat(instant-quotation): state how firm the estimate is |
| `4948e98` | 2026-07-28 | Migrated | Quotation | feat(instant-quotation): fold the material list and number the parts |
| `4b416b9` | 2026-07-28 | Migrated | Quotation | fix(instant-quotation): repair Thai rendering and viewer sizing |
| `09f04df` | 2026-07-28 | Migrated | Quotation | feat(instant-quotation): move parts beside the preview, rework the dropzone |
| `8f693a8` | 2026-07-28 | Migrated | Quotation | fix(instant-quotation): let the preview fill its column instead of leaving a void |
| `d161f80` | 2026-07-28 | Migrated | Quotation | fix(instant-quotation): stop the metrics card stretching to the summary |
| `7f15700` | 2026-07-28 | Excluded tooling | Foundation | Allow Impeccable live helper only in development |
| `6040ed5` | 2026-07-28 | Excluded tooling | Foundation | Enable Impeccable live helper across dev layouts |
| `0dac145` | 2026-07-28 | Migrated | Quotation | fix(instant-quotation): put the part's measurements beside the part |
| `4062184` | 2026-07-29 | Migrated | Quotation | Localize instant quotation legal links |
| `f506d03` | 2026-07-29 | Migrated | Quotation | fix(instant-quotation): fill material settings height |
| `172d08d` | 2026-07-29 | Migrated | Quotation | Support mobile CAD file selection and test submissions |
| `369b749` | 2026-07-29 | Migrated | Quotation | Recognize Safari STL file identifiers |
| `bb74b3e` | 2026-07-29 | Migrated | Foundation | Allow validated model selection on iOS Safari |
| `33ce2c0` | 2026-07-30 | Migrated | Quotation | Keep instant quotation summary within viewport |
| `da854df` | 2026-07-30 | Migrated | Quotation | fix(web): accept browser CAD uploads without content types |
| `ee2bb59` | 2026-07-30 | Migrated | Delivery | fix(web): correlate error pages with GKE logs |
| `a76d1c6` | 2026-07-30 | Migrated | Quotation | feat(web): configure build preference per quoted part |
| `297e8ab` | 2026-07-30 | Migrated | Foundation | fix(web): compact part settings and status overlay |
| `f9523e3` | 2026-07-30 | Migrated | Quotation | fix(web): make quotation repricing deterministic |
| `03e307f` | 2026-07-30 | Migrated | Foundation | fix(web): ignore degenerate wall-thickness boundary slices |
| `21ba323` | 2026-07-30 | Migrated | Quotation | fix(web): repair quotation scrolling and correct the material catalogue |
| `92122f4` | 2026-07-30 | Migrated | Quotation | feat(web): structure customer quotation details |
| `54d91d6` | 2026-07-30 | Migrated | Quotation | fix(web): rebuild the quotation configurator layout for tablets |
| `c26acfa` | 2026-07-31 | Migrated | Quotation | refactor(web): put the quotation configurator back on the design system |
| `f80b4b0` | 2026-07-31 | Release gate | Account | fix(web): bind recaptcha to workload identity |
| `0d80edc` | 2026-07-31 | Migrated | Foundation | feat(web): capture the customer details an order record actually needs |
| `e1c871c` | 2026-07-31 | Migrated | Public UI | feat(web): compare material prices automatically |
| `f3a853d` | 2026-07-31 | Migrated | Quotation | feat(web): persist instant quote customers and orders |
| `e09e3b0` | 2026-07-31 | Release gate | Delivery | fix(web): prefer service account for deployment |
| `d7e0296` | 2026-07-31 | Migrated | Quotation | Fix instant quotation gateway timeout handling |
| `beecf31` | 2026-07-31 | Migrated | Quotation | fix(web): correct thin-part DFM analysis |
| `ada8404` | 2026-07-31 | Migrated | Quotation | Complete instant quotation customer workflow |
| `344c32a` | 2026-07-31 | Migrated | Quotation | Reject invalid instant quote catalog fallbacks |
| `b220158` | 2026-07-31 | Migrated | Account | Fix emailed identity token round trips |
| `1c611bb` | 2026-07-31 | Release gate | Quotation | Reconcile instant quotation material catalog |
| `8888ce5` | 2026-07-31 | Excluded tooling | Foundation | Add repository Impeccable design tooling |
| `bea4a42` | 2026-07-31 | Migrated | Public UI | Refine service pages and add finishing guide |
| `1529bc5` | 2026-07-31 | Migrated | Public UI | Surface the finishing guide and refine service page hierarchy |
| `d46bf5e` | 2026-07-31 | Excluded tooling | Foundation | Record the post-fix design critique snapshot |
| `1498292` | 2026-08-01 | Migrated | Foundation | Make every section reachable from the TOC and publish tolerance guidance |
| `5e2030b` | 2026-08-01 | Release gate | Account | Fix all emailed Identity token callbacks |
| `beba894` | 2026-08-01 | Migrated | Account | Add secure email verification resend |
| `e62d177` | 2026-08-01 | Migrated | Foundation | Give the site a motion language and retire the template tells |
| `b16aa08` | 2026-08-01 | Migrated | Quality | Remove frontend dependency vulnerabilities |
| `04c9bb0` | 2026-08-01 | Migrated | Public UI | Add interactive HLC color matcher |
| `e25c833` | 2026-08-01 | Migrated | Account | Improve verification and first-login security |
| `81909e6` | 2026-08-01 | Migrated | Public UI | Redesign service chapter navigation |
| `2fbee81` | 2026-08-01 | Migrated | Public UI | Guide finishing color selection |
| `7055e4e` | 2026-08-01 | Migrated | Public UI | Measure finish matcher funnel |
| `a00a44e` | 2026-08-01 | Superseded safely | Foundation | Move the type system to self-hosted IBM Plex |
| `866fa2f` | 2026-08-01 | Migrated | Public UI | Polish finish color matcher guidance and preview |
| `7987661` | 2026-08-01 | Migrated | Public UI | Fix landing consultation button hover contrast |
| `94581dd` | 2026-08-01 | Migrated | Public UI | Fix finish matcher color fidelity and PBR interaction |
| `f6af750` | 2026-08-01 | Migrated | Public UI | Link Mesh Splitter from finishing guidance |
| `ad8cbb7` | 2026-08-01 | Migrated | Search/measurement | Allow Google AI crawler access |
| `ab2e481` | 2026-08-01 | Migrated | Account | Harden localized account and inquiry forms |
| `370fe20` | 2026-08-01 | Migrated | Foundation | Refresh shared UI and browser regression contracts |

## Machine verification

`CompleteSourceHistoryParityManifestTests` freezes the source head, exact
cohort counts, all 298 unique hashes, allowed dispositions, and the absence of
unresolved `Gap` rows. Source-aware verification recomputes the history
directly from read-only `R:\\maliev-web` before release.

This inventory proves review coverage, not production readiness. Aspire,
production-identical PostgreSQL reconciliation and rollback, consolidated-secret
projection, Workload Identity, Search Console/Safe Browsing, and owner approval
remain separate release gates.

