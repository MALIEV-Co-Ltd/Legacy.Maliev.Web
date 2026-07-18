# 3D Printing Static SSR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `/services/3d-printing` to Blazor static SSR while retaining the existing Razor Page as an equivalent feature-flagged rollback.

**Architecture:** Add one routed Razor component that owns document metadata and FAQ JSON-LD, while reusing `ThreeDimensionalPrintingContent` for the visible body. Extend the existing `BlazorRouting:Services` convention so the Razor endpoint is suppressed only while the static-SSR router is active; disabling the flag restores the unchanged Razor document.

**Tech Stack:** .NET 10, ASP.NET Core Razor Components static SSR, Razor Pages rollback, xUnit integration tests, System.Text.Json, deterministic esbuild assets, browser QA.

## Global Constraints

- Migrate exactly `/services/3d-printing`; do not move any additional route.
- Preserve exact English and Thai content, metadata, image preload, Service/FAQ/Breadcrumb JSON-LD, GTM/consent/contact analytics, CTA URLs, and accessibility behavior.
- Keep the route static-only with no Blazor runtime or interactive infrastructure.
- Keep `Pages/Services/3D-Printing.cshtml` as the canonical rollback source behind `BlazorRouting:Services=false`.
- Use the existing cluster and namespace only; do not deploy or create infrastructure or billable resources.
- Push a ready PR but do not merge it.

---

### Task 1: Capture 3D-printing route ownership and document contracts

**Files:**
- Create: `Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingStaticSsrRouteTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs`

**Interfaces:**
- Consumes: the existing Razor document at `/services/3d-printing`, `BlazorRouting:Services`, and shared public static-SSR shell markers.
- Produces: failing executable contracts for `ThreeDimensionalPrintingPage.razor`, route-owner instrumentation, exact localized metadata, schemas, analytics, static-only output, and Razor rollback.

- [x] **Step 1: Write the failing route contract**

Create `ThreeDimensionalPrintingStaticSsrRouteTests` with `WebApplicationFactory<Program>`. Assert the new route and owner source, exact EN/TH title, description, keywords and H1, `/src/images/services/printing/printing-hero.webp` preload, canonical/hreflang/Open Graph links, Service and three-question FAQ schemas, quotation CTAs, denied and accepted consent output, no framework assets, and feature-flagged Razor rollback. Update every exact routed-page allowlist to:

```csharp
[
    "CncMachiningPage.razor",
    "CustomManufacturingPage.razor",
    "ServicesPage.razor",
    "ThreeDimensionalPrintingPage.razor"
]
```

- [x] **Step 2: Verify the tests fail for the missing owner**

Run:

```powershell
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ThreeDimensionalPrintingStaticSsrRouteTests|FullyQualifiedName~BlazorHostFoundationTests|FullyQualifiedName~CncMachiningStaticSsrRouteTests|FullyQualifiedName~CustomManufacturingStaticSsrRouteTests|FullyQualifiedName~ServicesStaticSsrRouteTests"
```

Expected: failures because `ThreeDimensionalPrintingPage.razor`, its convention, its route owner, and the fourth approved routed page do not exist.

---

### Task 2: Implement the minimal static-SSR owner

**Files:**
- Create: `Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingPage.razor`
- Modify: `Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingContent.razor`
- Modify: `Legacy.Maliev.Web/Program.cs`

**Interfaces:**
- Consumes: `PublicOpenGraphMetadataDisplayModel`, `PublicDocumentLinksDisplayModel`, `PublicServiceStructuredDataDisplayModel`, and the existing localized body/FAQ wording.
- Produces: the routed static document and nullable `RouteOwner` parameter used only for migration provenance.

- [x] **Step 1: Add the route convention**

Inside the existing `if (useBlazorServicesRoute)` block, add:

```csharp
options.Conventions.AddPageRouteModelConvention(
    "/Services/3D-Printing",
    model => model.Selectors.Clear());
```

- [x] **Step 2: Add route-owner instrumentation without changing visible content**

Change the content root to:

```razor
<main class="service-page" data-migration-component="three-dimensional-printing-content" data-migration-route-owner="@RouteOwner">
```

and add:

```csharp
[Parameter]
public string? RouteOwner { get; set; }
```

- [x] **Step 3: Add the static-SSR page**

Create `ThreeDimensionalPrintingPage.razor` with `@page "/services/3d-printing"`, exact localized title/description/keywords copied from the Razor source, the printing hero preload, shared Open Graph/document-link/Service metadata components, the same three FAQ questions and answers serialized with `JsonSerializer`, and:

```razor
<ThreeDimensionalPrintingContent RouteOwner="blazor-static-ssr" />
```

- [x] **Step 4: Verify focused contracts are green**

Run the Task 1 command. Expected: all new and existing route-owner contracts pass with zero failures.

---

### Task 3: Validate, commit, and publish the review lane

**Files:**
- No additional production files.

**Interfaces:**
- Consumes: the completed route slice and issue #94.
- Produces: a clean commit, ready PR, Project #2 CI evidence, and no deployment.

- [x] **Step 1: Run repository validation**

```powershell
npm --prefix Legacy.Maliev.Web run build
npm --prefix Legacy.Maliev.Web audit --audit-level=high
dotnet format Legacy.Maliev.Web.slnx --no-restore --include Legacy.Maliev.Web/Program.cs Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingContent.razor Legacy.Maliev.Web/Components/Pages/Services/ThreeDimensionalPrintingPage.razor Legacy.Maliev.Web.Tests/ThreeDimensionalPrintingStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs --verify-no-changes
dotnet build Legacy.Maliev.Web.slnx -c Release --no-restore --nologo --warnaserror
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore
dotnet list Legacy.Maliev.Web.slnx package --vulnerable --include-transitive
```

Expected: deterministic assets unchanged, zero dependency vulnerabilities, clean format, zero warnings/errors, and the full suite green.

- [x] **Step 2: Run browser QA**

Verify EN desktop and TH 390x844 for one main/H1, keyboard skip link, heading order, duplicate IDs, image alternatives, table semantics, no overflow/logs, exact metadata, Service/FAQ/Breadcrumb schemas, consent behavior, bundled language font, and no framework scripts. Start a second host with `BlazorRouting__Services=false` and verify equivalent Razor rollback at the canonical URL.

- [ ] **Step 3: Commit and push**

Stage only the plan, route component/convention, content instrumentation, the new contract file, and the four exact allowlist updates. Commit as `feat(web): migrate 3D printing route to Blazor SSR`, then push `codex/blazor-3d-printing-route`.

- [ ] **Step 4: Open and validate the ready PR**

Open a ready PR that closes #94 and references #29. Add the PR to Project #2, set issue and PR to Validation/CI/Compatible, attach the exact replacement CI run, wait for PR validation and GitGuardian to pass, and do not merge or deploy.
