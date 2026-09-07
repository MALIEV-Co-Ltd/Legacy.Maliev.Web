# CNC signed-link contract

This slice migrates the source CNC notification dependency that resolves links
for finalized request files. It uses the existing authenticated FileService
`GET uploads/signedurl` contract and the fixed `maliev-quotation-requests`
bucket. It does not dispatch notifications or expose a browser endpoint.

The client accepts only canonical object names produced by
`CncFileFinalizationClient`, sends the service bearer token server-side, limits
the JSON response to 64 KiB, and returns only an absolute HTTPS URI without
userinfo. Missing tokens, rejected authorization, non-200 responses, wrong
content types, malformed JSON, unsafe links and transport failures fail closed.
Authorization failures invalidate the cached service token.

This client is deliberately separate from the member-download helper. CNC
notification links never permit HTTP and cannot be derived from browser-bound
bucket or object coordinates.
