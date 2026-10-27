# Web738 ownership and child-start diagnostics

PR738 normally integrates PR723 candidate
`539179229421c3170956583d0a9d54e9dca8aac6`, tree
`ac281a7e9e7a3c87328fe7af963c1bbf5c258c88`. That candidate's explicit
`TestingWebRootLease` ownership remains authoritative. The older provider-leaf
discovery proposal is not restored.

`TestingWebApplicationFactory` keeps the framework TestServer/Kestrel startup
path and owns its built host. Program creates the Testing-only web-root lease
before host construction. Failed construction/startup releases owned resources
while preserving the primary exception. Configured quotation children are
owned per case and use asynchronous disposal. Borrowed providers remain with
their original owner.

The retained `WebSurfaceOwnedLifetimeTests` cover direct and configured
factories, four quotation owners, failed startup and cleanup failure, lease
resolution failure, borrowed-provider isolation, and actual Kestrel dynamic
loopback/static responses. The inherited implementations and tests are
unchanged by the Career consumer delta. Exact-head full-suite proof is still
required; historical suite counts do not establish current acceptance.

PR738 separately retains three diagnostic lines in
`CncOwnedResourceScopeTests`: if child startup fails before the injected
stop-refusal callback supplies a recovery receipt, rethrow the exact captured
startup exception before the later null assertion can mask it. All existing
custody assertions remain. This is failure diagnosis in an existing test, not
CNC expansion or a cleanup waiver.

PR738 cannot merge until PR723 has accepted protected-main evidence. Career49
and58 cannot merge until the consumer is accepted on protected main. No
financial-stopped execution, deployment, production data, or security-policy
change is included.
