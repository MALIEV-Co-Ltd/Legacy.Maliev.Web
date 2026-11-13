# Explicit Testing web-root ownership

The integrated PR723 candidate
`539179229421c3170956583d0a9d54e9dca8aac6` owns the physical Testing web root at
creation through `Legacy.Maliev.Web/Infrastructure/TestingWebRootLease.cs`.
Program scopes that lease across build and application lifetime; the owning
test factory also preserves host cleanup on failed startup. Disposal is
idempotent and only applies to the provider created by the lease.

This supersedes the earlier proposal to discover physical leaves inside a
composite provider. A composite can contain borrowed or DI-owned providers;
matching a path does not establish ownership. The current implementation and
its tests preserve those providers and do not traverse opaque provider internals.

The framework distinction remains relevant: ASP.NET Core initializes web-root
providers and can wrap them for static assets, while a composite provider does
not take disposal ownership of all its children. The explicit lease avoids
inferring that ownership after construction. Reference sources:

- [ASP.NET Core hosting environment](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Hosting/Hosting/src/Internal/HostingEnvironmentExtensions.cs)
- [Static assets loader](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Hosting/Hosting/src/StaticWebAssets/StaticWebAssetsLoader.cs)
- [Composite provider](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.FileProviders.Composite/src/CompositeFileProvider.cs)

`WebSurfaceOwnedLifetimeTests` retain actual independent-host watcher checks,
failed-start cleanup with the primary exception intact, exact borrowed-provider
survival, non-Testing isolation, DI-owned disposal, and real dynamic-loopback
static response behavior. PR738 preserves those inherited source/test blobs;
it does not claim a new ownership implementation or acceptance from old logs.

The combined candidate still requires Release zero warnings/errors, the
focused ownership and Career boundaries, unchanged full suite and all required
formatting, security, browser, contract and coverage gates. Protected-main
acceptance is separate from component or source review. No resource limit,
watcher policy, timeout, skip, production behavior or deployment changes here.
