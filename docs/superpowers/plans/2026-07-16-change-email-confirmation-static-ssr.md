# Change Email Confirmation Static SSR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the invalid or expired `/Account/ChangeEmailConfirmation` result to localized Blazor static SSR without exposing the email address or single-use challenge token.

**Architecture:** The Razor PageModel remains the security boundary: it validates `email` and `token`, calls `ICustomerAuthenticationClient`, keeps `no-store` and `no-referrer`, and redirects successful challenges to login. A display-only static SSR component receives only sanitized validation-error strings; it never receives challenge values or authentication services.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages BFF, Blazor static SSR, `IStringLocalizer<T>`, xUnit, `WebApplicationFactory`.

## Global Constraints

- Preserve the public route `/Account/ChangeEmailConfirmation` and the successful redirect `/Account/Login?email={email}`.
- Keep `email`, `token`, service credentials, access tokens, refresh tokens, and customer identifiers out of the Razor component and its display model.
- Preserve `Response.Headers.CacheControl = "no-store"` and `Response.Headers["Referrer-Policy"] = "no-referrer"`.
- Preserve `[EnableRateLimiting("account")]` and missing-challenge `BadRequest()` behavior.
- Render with `<component type="typeof(ChangeEmailConfirmationContent)" render-mode="Static" param-Model="Model.DisplayModel" />`; do not load `blazor.web.js`.
- Keep `ViewData["Robots"] = "noindex, nofollow"`.
- Localize all visible Thai result text, including `The email-change link is invalid or expired.`.
- Use the existing GKE and repository workflow only; add no infrastructure, paid service, or direct database coupling.

---

### Task 1: Change-email confirmation result boundary

**Files:**
- Create: `Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationDisplayModel.cs`
- Create: `Legacy.Maliev.Web/Components/Pages/Account/ChangeEmailConfirmationContent.razor`
- Modify: `Legacy.Maliev.Web/Pages/Account/ChangeEmailConfirmation.cshtml`
- Modify: `Legacy.Maliev.Web/Pages/Account/ChangeEmailConfirmation.cshtml.cs`
- Move: `Legacy.Maliev.Web/Resources/Pages/Account/ChangeEmailConfirmation.th.resx` to `Legacy.Maliev.Web/Resources/Components/Pages/Account/ChangeEmailConfirmationContent.th.resx`
- Modify: `Legacy.Maliev.Web.Tests/BlazorMigrationContractTests.cs`
- Modify: `Legacy.Maliev.Web.Tests/WebSurfaceTests.cs`

**Interfaces:**
- Consumes: `ICustomerAuthenticationClient.CompleteEmailConfirmationAsync(string email, string token, CancellationToken cancellationToken)`.
- Produces: `ChangeEmailConfirmationDisplayModel(IReadOnlyList<string> Errors)` and `ChangeEmailConfirmationContent` with required `Model` parameter.

- [x] **Step 1: Write failing architecture and route tests**

Add a contract test that requires the static component host, server-only challenge handling, safe display model, component-owned Thai resource, no component authentication client, and no component email/token properties. Add English and Thai invalid-challenge surface tests using `token=invalid-token`; assert localized headings, localized error text, login link, `no-store`, `no-referrer`, no email/token in HTML, and no `blazor.web.js`. Keep the existing success redirect test.

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
$env:MalievWorkspaceRoot='B:\maliev'
dotnet test Legacy.Maliev.Web.Tests\Legacy.Maliev.Web.Tests.csproj --filter "FullyQualifiedName~ChangeEmailConfirmation" --logger "console;verbosity=minimal"
```

Expected: the new contract and invalid-result tests fail because `ChangeEmailConfirmationContent` and its display model/resource do not exist; the existing successful redirect test remains green.

- [x] **Step 3: Implement the minimal display-only component boundary**

Create:

```csharp
namespace Legacy.Maliev.Web.Components.Pages.Account;

public sealed record ChangeEmailConfirmationDisplayModel(IReadOnlyList<string> Errors);
```

Create a static component with marker `data-migration-component="change-email-confirmation-content"`. Render `Email address`, `Email change confirmation`, a validation summary whose items use `@Localizer[error]`, and a direct `/Account/Login` link. Add `DisplayModel` to the PageModel by flattening `ModelState` errors; do not include challenge parameters. Replace the page body with the static component tag helper and component localizer. Move the Thai resource and add `The email-change link is invalid or expired.` = `ลิงก์เปลี่ยนอีเมลไม่ถูกต้องหรือหมดอายุแล้ว`.

- [x] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command again.

Expected: all `ChangeEmailConfirmation` tests pass with zero failures.

- [x] **Step 5: Run release and browser gates**

Run deterministic asset build/drift check, Release build, the full test suite, `dotnet format --verify-no-changes`, npm and NuGet vulnerability audits, and gitleaks sequentially. Browser-check English desktop and Thai 390px invalid results; confirm localized copy, no horizontal overflow, no challenge value in the component DOM, and no `blazor.web.js`.

- [ ] **Step 6: Commit the reviewed slice**

Stage only the files listed above plus this plan and commit:

```powershell
git commit -m "feat(web): migrate change-email confirmation to static SSR"
```

Push a `codex/` branch, create a ready PR referencing issue #29, add it to organization Project #2, monitor PR checks, rebase-merge under the sole-developer policy, verify exact-SHA main CI and immutable-image publication, then mark the Project item Done with evidence.
