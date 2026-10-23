#
Current integration disposition: PR761 candidate `63a1d4b8972ad24262dbcedd8ef9739c4aa6da5a` supersedes this historical ownership proposal with explicit `TestingWebRootLease` creation ownership. The earlier provider-discovery implementation is not restored. Its ten cases map to retained `WebSurfaceOwnedLifetimeTests`: two direct/configured factories, four quotation owners, four failed-start combinations (including the two shared-factory cases), lease-resolution failure, and actual Kestrel dynamic-port/static-response behavior. Those checks also preserve borrowed-provider isolation. PR738 retains only its three-line original child-start failure diagnostic beyond the PR761 ownership implementation. Exact-head and protected-main validation remain required.
WebSurface physical web-root ownership proposal

Status: source-only proposal based on Web head `859db9b6d0d35ffa036384bf9695208692a605a8`, tree `bc8b81bad11f013caa73e673e316a664472b4d01`. No build, SDK, test host, CI dispatch, production change, or financial/provider execution was performed by the author.

## Retained failure evidence

Root's sealed complete raw log blob `5147e40a592107e67eb173f00fc654d0406698da` records run 39690235303 / job 119534340308. Release completed with zero warnings/errors and the focused stages passed. The full suite ended with 1384 failed, 2924 passed, 0 skipped, 4308 total.

The first visible failure is a WebSurface request returning 500 at 2026-10-21T23:22:56.9708101Z. At 23:22:57.8959034Z the first explicit inotify 128 exception occurs in the default JSON configuration watcher during Program.cs21 / WebApplication.CreateBuilder, before OwnedWebSurfaceParent's builder.Build returns. The console does not contain the earlier 500 server exception. It does not prove every 500 has the same cause.

## Missing ownership in pinned sources

The exact Web head owns and disposes each WebSurface parent, child factory, client and built host. The separate physical web-root provider is not owned.

In dotnet/aspnetcore v10.0.12, HostingEnvironmentExtensions initializes a separate physical wwwroot provider. Program's existing Testing-only UseStaticWebAssets wraps it in CompositeFileProvider. The composite exposes its children and forwards Watch to them, but implements no disposal. The manifest provider's Watch returns NullChangeToken. In dotnet/runtime v10.0.12, Host.DisposeAsync disposes the content-root/default providers and service provider, without traversing the web-root composite. HostingEnvironment itself is not disposable.

This establishes a missing disposal owner for the physical web-root watcher. Accumulation causing the observed resource exhaustion is the evidence-backed explanation to validate on the hosted runner; a hosted before/after handle census was not available.

Source references:
- https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Hosting/Hosting/src/Internal/HostingEnvironmentExtensions.cs
- https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Hosting/Hosting/src/StaticWebAssets/StaticWebAssetsLoader.cs
- https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Shared/StaticWebAssets/ManifestStaticWebAssetFileProvider.cs
- https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.FileProviders.Composite/src/CompositeFileProvider.cs
- https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.Hosting/src/Internal/Host.cs

## Bounded correction and regressions

Only the existing independent WebSurface TestServer owner changes. An IHost adapter captures public physical web-root leaves, deduplicates by object identity, excludes the content-root provider already owned by the framework, and disposes the host before those leaves. All disposals are attempted even if host disposal fails. The existing startup exception remains primary. Opaque manifest internals and other test factories are untouched.

One actual-host case activates physical watches in two independent existing WebSurface owners. Disposal must prevent new watches on the disposed case while the other case remains usable, then also release that second case. Two synthetic startup-failure rows prove duplicate nested leaves are disposed exactly once even when host cleanup throws. Existing case bodies and assertions are preserved.

No production source, dependency, workflow, limit, polling option, concurrency control, filter, skip, required check, or financial behavior changes. The 4308 existing cases remain; the three new rows make an expected 4311 cases, subject to hosted discovery.

## Required hosted follow-up

Root owns publication and GitHub Actions. Build Release with zero warnings/errors first, then run WebSurfaceOwnedLifetimeTests and WebSurfaceTests, the unchanged full suite and all existing required formatting/static/contract/browser/coverage gates on the exact proposed head. All author-side .NET checks are **not run** in the source-only/resource-constrained lane. Pure JavaScript source-preservation checks and immutable blob/tree readbacks are separate evidence, not build or test passes.

Career producer publication remains held until the Web consumer reaches accepted protected main. Comment/TrackingNumber, FX, financial/provider execution, deployment and production data remain blocked.
