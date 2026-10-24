# Client-first Career original read compatibility

Status: normal dependent integration of PR723 candidate `539179229421c3170956583d0a9d54e9dca8aac6`, tree `ac281a7e9e7a3c87328fe7af963c1bbf5c258c88`. Combined validation and protected-main acceptance remain pending.
Publish this Web consumer slice before the paired Career producer slice.
No deployment or financial/provider execution is included.

## Bounded source and target contract

Original initial `5fac706a7983a6d359b39acbd670e6800afe020e` (no parents) and
retained checkpoint `770cb58032969ae4a0fe169686b53ab61a44fff8` (parent
`f96a6cbe23407ac25af3ebc4bc669296a09ac164`) have identical
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
Current read-only source head `d23065d499333ece5ef73013b085a8aaa062b163`,
parent `449d87ea9d1d6ab40f1ec444353ab6aba80a5a76`, retains the same
`Maliev.JobService.Api/Controllers/JobsController.cs` blob
`dfcae032646d4951cb0eb6727c9f086407a6fdb3` and
`Maliev.Entities/ViewModels/PaginatedListWebApi.cs` blob
`9684bc3f0ba2cd399621b3891c00b1b05ba6b398`. No original files were changed.

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

The retained PR723 integration restores the original detail flow in both
CareerDetailPage.razor and Pages/Career/View.cshtml.cs: each starts the offer
read and an independent levels read, then presents the matching LevelId from
that independent list. It deliberately ignores a conflicting nested Level.
The 24 existing actual-HTTP cases cover both renderers, English/Thai, omitted
and conflicting nested levels, missing metadata, null names and 404/503 levels.
They require exactly one offer request and one levels request.

The older PR738 proposal enriched GetOfferAsync with another levels request.
That proposal predates the restored source flow and would now make three calls.
This integration retains the raw detail reader and the independent presentation
lookup byte-for-byte from PR723. Four raw-reader cases additionally preserve
omitted, null, empty and supplied nested Level values without another request.
The 19 old enrichment expectations are replaced because they described the
superseded implementation, not the original two-request route contract.

## Validation and publication order

The count reader retains its 23 malformed/duplicate/alias/status/query/cancellation
rows. The Career focused workflow adds those 23 and the four raw-detail rows to
the existing 213 cases; expected focus is 240, subject to actual discovery.
The original four Offers materialization oracles and all 24 detail HTTP rows
remain unchanged. No test or coverage threshold, timeout or skip is relaxed.

PR738 may validate this combined candidate while PR723 is active. Its merge is
held until PR723 reaches protected main with all required exact-main checks.
Then PR738 requires fresh exact-head Release with zero warnings/errors, the
complete 240-case consumer identity set and four original oracle identities,
full tests, formatting, static/security, contract/browser and coverage gates,
normal protected merge and exact-main checks. CareerService49 (pagination) and
58 (detail) remain separate producer obligations behind accepted Web738 main.

The fixed consumer roster combines actual retained213 and new27 execution
identities; the separate oracle retains four actual identities. The new strict
qualifier reuses the existing native TRX checks and also requires every exact
parameterized name. Synthetic parser controls establish gate behavior only.

The integrated local Release build passed with zero warnings/errors. Its
240-case focus first returned an unexpected500 and then explicit
`OutOfMemoryException` failures during MVC/Razor host creation under the
unchanged1.5GiB private-job cap. The exact owned root was stopped and its private
job cleanup verified. This is not passing focus evidence. The new exact240 and
original4 gates, full suite and every existing policy remain required on hosted
CI; no memory guard, timeout or assertion was relaxed.

The staged totalItems alias is bounded to this reader. Its later removal needs
confirmed retirement of old producer responses. No producer response alias,
DTO rewrite, cache, authentication or global error-policy change is included.
Original source SHA obligations retain their separate owners; this consumer
bundle does not retire whole mixed commits, shared helpers or all Career actions.
