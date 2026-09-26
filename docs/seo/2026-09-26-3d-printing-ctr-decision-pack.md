# 3D Printing CTR ownership and controlled-test decision pack

Migration provenance: original `maliev-web` commits `31ba7d7c9816330529c8c335939fde5d0fc4a632` and `0665dcd54788c037ee663ff90f32741014f0c81c`. Search Console figures below are a dated source snapshot, not a claim of current performance. The proposed snippet remains unreleased in Legacy.Maliev.Web.

Date: 2026-09-26
Decision owner: MALIEV owner, Marketing, and Web
Canonical owner: `https://www.maliev.com/services/3d-printing`
Status: prepared; no metadata publication before the evidence gate

## Decision boundary

This page owns Thai and English commercial intent for 3D-printing services: `รับพิมพ์ 3D`, `รับปริ้น 3D`, spelling variants, FDM, resin, SLS/MJF, SLM/DMLS, prototypes, and production parts. Educational articles may support the cluster and link here; they must not become competing service owners.

Search-result CTR is clicks divided by impressions. It is complete before a visitor enters the website. Route-choice, quotation-stage, persisted-lead, employee-qualified quotation, and qualified-customer measures remain separate.

## Current evidence

Source capture: authenticated Search Console artifact generated 2026-09-26, complete through 2026-09-23 with a three-day lag.

| Window/query | Clicks | Impressions | CTR | Weighted position | Classification |
|---|---:|---:|---:|---:|---|
| 3D-printing owner cohort, complete 7d | 1 | 68 | 1.47% | 11.10 | verified fact |
| 3D-printing owner cohort, complete 28d | 4 | 315 | 1.27% | 11.13 | verified fact |
| `ปริ้น 3d`, complete 28d | 1 | 300 | 0.33% | 6.85 | verified weak-CTR signal |
| `ปริ้น3d`, complete 28d | 1 | 135 | 0.74% | 7.19 | verified weak-CTR signal |
| `ปริ้น 3d ราคา`, complete 28d | 0 | 83 | 0.00% | 8.25 | verified weak-CTR signal |
| `ปริ้น3d ราคา`, complete 28d | 0 | 68 | 0.00% | 8.66 | verified weak-CTR signal |
| `รับพิมพ์ 3d`, complete 28d | 0 | 58 | 0.00% | 10.33 | verified weak-CTR signal |

The current average position explains part of the low cohort CTR. The weak result for price-oriented queries already in positions 6–9 is a signal that the snippet/offer may not win the click; it is not proof of one customer motive or competitor advantage.

The current title, H1, description, canonical, sitemap membership, and Google-selected canonical were released or verified recently. URL Inspection passed with the declared and Google-selected canonical equal to the owner URL.

## Current ownership matrix

| Intent | Primary owner | Supporting route | Conversion route | Required measurement |
|---|---|---|---|---|
| FDM/resin with a supported file | `/services/3d-printing` | process/material sections | `/instantquotation/3d-printing` | `quote_route_selected(route=instant)` then page/stage/persisted events |
| Industrial polymer or metal additive | `/services/3d-printing` | engineering-review section | `/quotation?item=3D-Printing` | `quote_route_selected(route=engineering)` then persisted manual quotation |
| Process unknown or multi-process | `/services/custom-manufacturing` | links to specialist owners | manual engineering review | service-local route and persisted outcome |
| 3D scanning/reverse engineering | `/services/3d-scanning` | 3D-printing internal link | scanning quotation | separate scanning measures |
| CNC alternative | `/services/cnc-machining` | 3D-printing internal link | CNC quotation | separate CNC measures |

## Verified proof register

| Proof | Approved use | Exclusions |
|---|---|---|
| Owner-confirmed production photographs under `M:\20_Media\Photos\parts-photography`, excluding `MALIEV_Processed` | First-party examples after owner review and privacy check | No customer, material, tolerance, certification, or performance inference from an image |
| FDM and resin instant-estimate route | State that supported files and listed materials can receive a preliminary online estimate | Do not promise instant pricing for SLS, MJF, SLM, DMLS, safety-critical, or engineering-review work |
| Engineering-review route | State that process, material, finishing, inspection, quantity, and lead time are confirmed per project | Do not imply feasibility before files and requirements are reviewed |
| No minimum quantity | State that one-piece enquiries are accepted | Do not imply every geometry/process has identical unit economics |
| Thailand shipping from THB 100 and Pak Kret pickup by appointment | Use as a verified logistics proposition | Do not convert nationwide shipping into nationwide GBP service-area coverage |
| Supported upload formats | STL, OBJ, 3MF, GLB, GLTF, STEP/STP, IGES/IGS | Extension support does not prove valid geometry or automatic price eligibility |

Do not claim equipment counts, facility scale, certifications, in-house SLM/DMLS ownership, customer identities, guaranteed tolerances, guaranteed lead time, or guaranteed feasibility without a separate authoritative source.

## Controlled Thai snippet test

The current production metadata remains unchanged until the evidence gate below passes. If the mature window still shows weak CTR for positions 4–10, the first bounded test is title and description only; canonical, H1, schema ownership, page body, destinations, and routes stay unchanged.

Current Thai title:

`รับปริ้น 3D รับพิมพ์ 3 มิติ | ประเมินราคาออนไลน์ | MALIEV`

Prepared test title:

`รับปริ้น 3D รับพิมพ์ 3 มิติ | FDM เรซิ่น เริ่ม 1 ชิ้น | MALIEV`

Prepared test description:

`รับปริ้น 3D งาน FDM และเรซิ่น เริ่ม 1 ชิ้น ไม่มีขั้นต่ำ อัปโหลด STL, STEP, OBJ หรือ 3MF เพื่อประเมินราคาออนไลน์ พร้อมจัดส่งทั่วไทยจาก MALIEV`

This proposal emphasizes verified buyer-facing facts without a cheap-price claim. It must be rechecked against Google's displayed snippet and actual page text immediately before any approved release.

## Evidence and approval gate

No production title, H1, meta description, schema, canonical, internal link, sitemap, content, or indexing change may be made from this document before all of these conditions hold:

1. Search Console contains a complete 28-day window after the 2026-09-18 release, plus normal lag, expected around 2026-10-19/20.
2. The owner cohort and price-query rows are compared with the equivalent prior complete window by clicks, impressions, CTR, and weighted position.
3. Queries in positions 4–10 with weak CTR remain the test cohort; ranking loss is not misdiagnosed as snippet loss.
4. The exact proposed title/description receive action-time owner approval.
5. Release validation covers English/Thai render, title/description length and meaning, canonical/hreflang/schema/sitemap, mobile/desktop layout, and both quote destinations.

## Measurement plan

- Organic: query/page clicks, impressions, CTR, weighted position, displayed owner URL, and Google-selected canonical.
- Route choice: `quote_route_selected` by `service_id=3d_printing`, `route`, `placement`, locale, device, and GA4 session source/medium/campaign when available.
- Instant quotation: landing page view, `quote_started`, `quote_stage_result` by stage/result/failure category, upload completion, estimate shown, review reached, submit attempt/error, and persisted lead joined only by the existing privacy-safe `journey_id` boundary.
- Business: employee-qualified persisted quotations and qualified customers remain separate authoritative outcomes; no click or technical event is promoted to either.
- Decision window: one complete 28-day post-change window plus Search Console lag; retain a seven-day directional check that cannot trigger another rewrite by itself.

## Rollback and non-actions

If a future snippet test reduces non-branded CTR without an offsetting improvement in qualified persisted quotations, or creates ownership/canonical/render drift, revert only that metadata commit and redeploy the prior known-good image.

This decision pack does not authorize production metadata publication, application deployment, Search Console submission, GTM/GA4 publication, Ads changes, GBP changes, Shopify changes, outreach, or billing actions. Migrating this document into protected main does not open those release gates.
