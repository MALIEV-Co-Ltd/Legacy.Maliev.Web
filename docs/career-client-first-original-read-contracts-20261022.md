# Client-first Career original read compatibility

Status: dependent integration against PR761 candidate 63a1d4b8972ad24262dbcedd8ef9739c4aa6da5a; protected-main acceptance and exact-head validation pending.
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

The PR761 candidate restores the original detail flow in both
CareerDetailPage.razor and Pages/Career/View.cshtml.cs: each starts the offer
read and an independent levels read, then presents the matching LevelId from
that independent list. It deliberately ignores a conflicting nested Level.
The 24 existing actual-HTTP cases cover both renderers, English/Thai, omitted
and conflicting nested levels, missing metadata, null names and 404/503 levels.
They require exactly one offer request and one levels request.

The older PR738 proposal enriched GetOfferAsync with another levels request.
That proposal predates the restored source flow and would now make three calls.
This integration retains the raw detail reader and the independent presentation
lookup byte-for-byte from PR761. Four raw-reader cases additionally preserve
omitted, null, empty and supplied nested Level values without another request.
The 19 old enrichment expectations are replaced because they described the
superseded implementation, not the original two-request route contract.

## Validation and publication order

The count reader retains its 23 malformed/duplicate/alias/status/query/cancellation
rows. The Career focused workflow adds those 23 and the four raw-detail rows to
the existing 213 cases; expected focus is 240, subject to actual discovery.
The original four Offers materialization oracles and all 24 detail HTTP rows
remain unchanged. No test or coverage threshold, timeout or skip is relaxed.

PR761 must first reach protected main. Then this consumer requires exact-head
Release with zero warnings/errors, focused and full tests, formatting,
static/security, contract/browser and coverage gates, normal protected merge
and exact-main checks. CareerService #58 stays behind accepted consumer main.
Local Release builds completed with zero warnings/errors; all 27 client cases and the four original materialization oracles passed with zero skips. The combined 240-case route/client focus encountered System.OutOfMemoryException under the unchanged 1.5 GiB private-job cap and was stopped; it is not a passing result. Complete hosted focus, full-suite and protected-main evidence remain pending.

The staged totalItems alias is bounded to this reader. Its later removal needs
confirmed retirement of old producer responses. No producer response alias,
DTO rewrite, cache, authentication or global error-policy change is included.
Original source SHA obligations retain their separate owners; this consumer
bundle does not retire whole mixed commits, shared helpers or all Career actions.
