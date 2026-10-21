# Client-first Career original read compatibility

Status: source proposal, independent review and hosted execution pending.
Publish this Web consumer slice before the paired Career producer slice.
No deployment or financial/provider execution is included.

## Bounded source and target contract

Original initial `5fac706a7983a6d359b39acbd670e6800afe020e` (no parents) and
retained final `770cb58032969ae4a0fe169686b53ab61a44fff8` have identical
JobsController `dfcae032646d4951cb0eb6727c9f086407a6fdb3` and shared
PaginatedListWebApi `9684bc3f0ba2cd399621b3891c00b1b05ba6b398`.
The original anonymous Jobs page has six camelCase envelope fields:
items:offer[], pageIndex:int, totalPages:int, totalRecords:int,
hasPreviousPage:bool and hasNextPage:bool. TotalRecords is the filtered count;
omitted index is1, omitted size is filtered count, pageIndex is clamped to1;
empty/exhausted results return404. Existing producer paging calculations and
query/sort/404 behavior are preserved; only its count wire name will change.

Original fresh-context offer detail uses Offers.FindAsync. Initial/final startup
`c8601eccfa44a53e2a9784b7231ee3beb043ef9d` /
`e4b34a9d725cea6ccb7c22e3bd8c0da4fe390fb8`, service registration
`067f052db0f4103303345f0c849bd5ec993fbc94` /
`93204f4c523d3d90e88fabf6442231fcc2010059`, ordinary SQL registration and
final context `9181243e3ad6c311beeb34b42d4a18548afb336c` establish no eager,
automatic or lazy navigation load for that action. Offer initial/final models
`f950dafc431b7a4a7c1db1b783b7787b80884e0e` /
`3ad03e62453c9c72f5c807e4a61ed96433afb129` have no loader injection.
Virtual navigation alone does not enable loading. Bounded data/model inventory
contains no partial model override. Initial/final project references declare
no proxy package, and inspected setup has no proxy enablement or AutoInclude.
Original null-ignore MVC therefore omits detail.level.
List explicitly includes Level and clears Level.Offers; those list fields stay.

The source model/documentation refresh
`72eb9f1949176392141951d35e6e06f7c30af4c2`, parent
`023aa2f143fa5a8f557d72c1302b9add64792d6a`, remains individually attributed.
This bounded read contract does not retire that mixed source, the initial SHA,
the shared helper's other owners or all Career actions.

## Actual current consumers and changes

Web base main `ca1b5c501647ce9ecd6b206acb6be7be94657bce`, tree
`e9f5a1ee2e7167999bb42b87fc8792a06e7abc3b`.
CareerClient preimage `09b57190f54285be5220d2866a80dd6309edc8f3` reads
CareerOfferPage.TotalItems through the default HTTP JSON reader. Contract blob
`13b82e34ffb4fd87539e6f15655d3affbff40267` retains the existing CLR/UI count.
Changing only the producer count name would silently default this reader to0.

The new page-only reader recognizes canonical totalRecords and staged
totalItems, preserving existing case-insensitive Web reader behavior. Canonical
is preferred; if both names exist their parsed values must agree. Duplicate
count keys, missing counts, null, invalid integer/type/range, negative values
and conflicting aliases throw JsonException. No present invalid canonical count
falls back to a valid legacy count. Existing Web numeric-string integer decoding
is retained; the canonical producer still emits an unquoted integer.
All other page fields use the same existing Web deserializer. No global DTO
rewrite, naming policy, cache or dependency change is introduced.

Malformed counts propagate JsonException just as existing malformed integer/JSON
payloads did; they are not converted to successful zero or a new503 response.
Existing404 creates the same available empty page. HTTP failures/unowned timeout
retain the existing unavailable result; caller cancellation still propagates.

CareerDetailContentModel `5a6f82b31b9898da36cb382932da4f19d2bf880d` uses
offer.Level?.Name or the existing "not specified" display. It is shared by
current SSR detail `2782ac51ea8eaaa7a36e4a3539dd177ca81be004` and Razor
rollback page `2e005a4c2383b07e64e5cce4af5678dbe0513702`.
A producer-only level omission would lose that displayed metadata.
The consumer now consults the existing anonymous levels endpoint only when the
detail has no Level and LevelId is positive. An already supplied Level causes
no extra request. One exact matching level may enrich the client-local model;
no arbitrary duplicate winner or invented name is used.

Missing/404/unavailable/empty/null/ambiguous metadata leaves the offer available.
Malformed JSON, unsupported decode or invalid charset also leaves the offer
available with the existing display fallback. Those catches are scoped to the
new secondary lookup; malformed original offer/page responses keep their prior
semantics. A nullable matched Name remains nullable. Caller cancellation during
the lookup propagates. Existing GetLevelsAsync handles the remote transport;
there is no new endpoint, authentication, cache or global error classifier.

Scoped organization ICareerClient search identified only Web production and
test references. The actual typed client, page contracts, both detail render
owners and their shared presentation model were inspected. This is source
consumer qualification, not a live deployed join or financial acceptance.

## Authored checks and publication order

23 count-reader/query/error/status/cancellation rows and19 detail-metadata/
display/error/cancellation cases are authored, not executed. They drive the
actual CareerClient with controlled HTTP transport and the existing shared
CareerDetailContentModel. No UI route files or DTO contracts are modified.
Each test disposes its HTTP client/handler and cancellation source; no host,
provider fixture or new worker is started by these tests.

Root routes this exact source to the existing Web owner for independent review.
Required exact-head Release0warnings/errors, focused42 new cases plus existing
CareerClient/SSR/Razor tests, admitted full Web suite and unchanged formatting,
static/security/audit/contract/browser/coverage gates remain mandatory.
Verify the protected consumer main and its checks first. Then publish the
Career producer correction against fresh main and run its exact-head/main gates.
Do not substitute a permanent producer response alias or lower checks because
Web baseline validation is difficult. No hosted or local pass is claimed here.

Legacy totalItems support is deliberately bounded to this reader during staged
mains; removing it later requires separate confirmed consumer retirement.
All financial/provider/frozen FX and Comment/TrackingNumber holds remain inactive.
Root remains sole publisher, canonical writer and official collector.
