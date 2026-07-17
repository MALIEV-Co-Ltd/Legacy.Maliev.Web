# CNC Machining Static SSR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `/services/cnc-machining` to Blazor static SSR while retaining the existing Razor page as an equivalent feature-flagged rollback.

**Architecture:** Add one routed Razor component that owns document metadata and FAQ JSON-LD, while reusing the existing `CncMachiningContent` component for the visible body. Extend the existing `BlazorRouting:Services` convention so the Razor endpoint is suppressed only while the static-SSR router is active; disabling the flag restores the unchanged Razor document.

**Tech Stack:** .NET 10, ASP.NET Core Razor Components static SSR, Razor Pages rollback, xUnit integration tests, System.Text.Json, deterministic esbuild assets, browser QA.

## Global Constraints

- Migrate exactly `/services/cnc-machining`; do not move any additional route.
- Preserve exact English and Thai content, metadata, image preload, Service/FAQ/Breadcrumb JSON-LD, GTM/consent/contact analytics, CTA URLs, and accessibility behavior.
- Keep the route static-only with no Blazor runtime or interactive infrastructure.
- Keep `Pages/Services/CNC-Machining.cshtml` as the canonical rollback source behind `BlazorRouting:Services=false`.
- Use the existing cluster and namespace only; do not deploy or create infrastructure or billable resources.
- Push a ready PR but do not merge it.

---

### Task 1: Capture CNC route ownership and document contracts

**Files:**
- Create: `Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs`

**Interfaces:**
- Consumes: the existing Razor document at `/services/cnc-machining`, `BlazorRouting:Services`, and the shared static-SSR shell markers.
- Produces: failing executable contracts for `CncMachiningPage.razor`, route-owner instrumentation, exact localized metadata, schemas, analytics, static-only runtime, and Razor rollback.

- [x] **Step 1: Write the failing route contract**

Create `CncMachiningStaticSsrRouteTests` using `WebApplicationFactory<Program>`. Assert that:

```csharp
Assert.Contains("/Services/CNC-Machining", program, StringComparison.Ordinal);
Assert.Contains("@page \"/services/cnc-machining\"", route, StringComparison.Ordinal);
Assert.Contains("RouteOwner=\"blazor-static-ssr\"", route, StringComparison.Ordinal);
Assert.Contains("<PublicServiceStructuredData", route, StringComparison.Ordinal);
Assert.Contains("FAQPage", route, StringComparison.Ordinal);
Assert.Contains("data-migration-route-owner=\"@RouteOwner\"", content, StringComparison.Ordinal);
Assert.Equal(
    ["CncMachiningPage.razor", "CustomManufacturingPage.razor", "ServicesPage.razor"],
    routedPages);
```

Add EN/TH document theories for these exact values:

```text
EN title: CNC Machining Services in Bangkok & Nonthaburi | One-Off and Production Parts
EN description: CNC milling and turning for one-off parts, prototypes, jigs and production. Common JIS metals and engineering plastics. Send CAD and drawings for a quote.
EN keywords: CNC machining Thailand, CNC aluminum, CNC one piece, machine shop Bangkok, CNC Nonthaburi
TH title: รับงาน CNC ตามแบบ กรุงเทพและนนทบุรี | งานชิ้นเดียวถึงงานผลิต
TH description: MALIEV รับผลิตชิ้นงาน CNC ตามไฟล์ CAD และแบบงาน ตั้งแต่งานชิ้นเดียว ต้นแบบ จิ๊ก ไปจนถึงงานผลิตซ้ำ รองรับโลหะและพลาสติกวิศวกรรม
TH keywords: รับ CNC อลูมิเนียม, รับกลึง CNC, โรงกลึง นนทบุรี, CNC งานชิ้นเดียว, โรงงาน CNC
```

Assert the hero preload, canonical/hreflang/Open Graph, Service/FAQ JSON-LD, quotation/contact links, denied and accepted consent behavior, no framework assets, and the feature-flagged Razor rollback.

- [x] **Step 2: Verify the tests fail for the missing owner**

Run:

```powershell
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~CncMachiningStaticSsrRouteTests|FullyQualifiedName~BlazorHostFoundationTests|FullyQualifiedName~ServicesStaticSsrRouteTests"
```

Expected: failures because `CncMachiningPage.razor`, its route convention, and the third approved routed page do not exist.

---

### Task 2: Implement the minimal static-SSR owner

**Files:**
- Create: `Legacy.Maliev.Web/Components/Pages/Services/CncMachiningPage.razor`
- Modify: `Legacy.Maliev.Web/Components/Pages/Services/CncMachiningContent.razor`
- Modify: `Legacy.Maliev.Web/Program.cs`

**Interfaces:**
- Consumes: `PublicOpenGraphMetadataDisplayModel`, `PublicDocumentLinksDisplayModel`, `PublicServiceStructuredDataDisplayModel`, and the existing localized body/FAQ wording.
- Produces: the routed static document and nullable `RouteOwner` parameter used only for migration provenance.

- [x] **Step 1: Add the route convention**

Inside the existing `if (useBlazorServicesRoute)` block, add:

```csharp
options.Conventions.AddPageRouteModelConvention(
    "/Services/CNC-Machining",
    model => model.Selectors.Clear());
```

- [x] **Step 2: Add route-owner instrumentation without changing visible content**

Change the root element to:

```razor
<main class="service-page" data-migration-component="cnc-machining-content" data-migration-route-owner="@RouteOwner">
```

and add:

```csharp
[Parameter]
public string? RouteOwner { get; set; }
```

- [x] **Step 3: Add the static-SSR page**

Create `CncMachiningPage.razor` with `@page "/services/cnc-machining"`, localized title/description/keywords copied exactly from the Razor source, and:

```razor
<PageTitle>@title</PageTitle>
<HeadContent>
    <meta name="description" content="@description" />
    <meta name="keywords" content="@keywords" />
    <link rel="preload" as="image" href="/src/images/services/cnc/cnc-hero.webp" />
    <PublicOpenGraphMetadata Model="@(PublicOpenGraphMetadataDisplayModel.Create(context, title, description, image: null))" />
    <PublicDocumentLinks Model="@(PublicDocumentLinksDisplayModel.Create(context))" />
    <PublicServiceStructuredData Model="@(PublicServiceStructuredDataDisplayModel.Create("CNC Machining"))" />
    <script type="application/ld+json">@((MarkupString)faqJson)</script>
</HeadContent>
<CncMachiningContent RouteOwner="blazor-static-ssr" />
```

Serialize the same three FAQ questions and answers as `Pages/Services/CNC-Machining.cshtml` using `JsonSerializer.Serialize` and `Question`/`Answer` dictionaries.

- [x] **Step 4: Verify focused contracts are green**

Run the Task 1 command. Expected: all CNC, host, and services route tests pass with zero warnings.

---

### Task 3: Validate, commit, and publish the review lane

**Files:**
- No additional production files.

**Interfaces:**
- Consumes: the completed route slice and issue #92.
- Produces: a clean commit, ready PR, Project #2 CI evidence, and no deployment.

- [x] **Step 1: Run repository validation**

```powershell
npm run build
npm audit --audit-level=high
dotnet format Legacy.Maliev.Web.slnx --no-restore --include Legacy.Maliev.Web/Program.cs Legacy.Maliev.Web/Components/Pages/Services/CncMachiningContent.razor Legacy.Maliev.Web/Components/Pages/Services/CncMachiningPage.razor Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs --verify-no-changes
dotnet build Legacy.Maliev.Web.slnx -c Release --no-restore --nologo --warnaserror
dotnet test Legacy.Maliev.Web.Tests/Legacy.Maliev.Web.Tests.csproj -c Release --no-build --no-restore
```

Expected: deterministic assets unchanged, zero vulnerabilities, clean format, zero warnings/errors, and the full suite green.

- [x] **Step 2: Run browser QA**

Verify EN desktop and TH 390x844 for one main/H1, keyboard skip link, image alternatives, heading order, no duplicate IDs, no overflow/logs, exact metadata, Service/FAQ/Breadcrumb schemas, consent behavior, bundled language font, and no framework scripts. Start a second host with `BlazorRouting__Services=false` and verify equivalent Razor rollback at the canonical URL.

- [ ] **Step 3: Commit and push**

```powershell
git add docs/superpowers/plans/2026-07-17-cnc-machining-static-ssr.md Legacy.Maliev.Web/Program.cs Legacy.Maliev.Web/Components/Pages/Services/CncMachiningContent.razor Legacy.Maliev.Web/Components/Pages/Services/CncMachiningPage.razor Legacy.Maliev.Web.Tests/CncMachiningStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/BlazorHostFoundationTests.cs Legacy.Maliev.Web.Tests/CustomManufacturingStaticSsrRouteTests.cs Legacy.Maliev.Web.Tests/ServicesStaticSsrRouteTests.cs
git commit -m "feat(web): migrate CNC machining route to Blazor SSR"
git push -u origin codex/blazor-cnc-machining-route
```

- [ ] **Step 4: Open and validate the ready PR**

Open a ready PR that closes #92 and references #29. Attach the exact replacement CI run to Project #2, set the item to Validation/CI/Compatible, wait for PR validation and GitGuardian to pass, and do not merge or deploy.
