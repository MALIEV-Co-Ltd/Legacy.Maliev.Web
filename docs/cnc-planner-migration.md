# CNC planner source migration

Source snapshot: `5ac7d045c51194edd9e64d8564f1b726b001be34`.

The CNC computation modules and model-viewer worker preserve the source contents
(with line-ending normalization only). CAD fixtures are extracted from committed
Git objects. Node regressions change only the repository directory prefix.

This slice includes 41 independent source regression files. Three page-dependent
files remain with the pending page migration: cnc-access.test.cjs,
cnc-ball-handoff-allocation.test.cjs, and cnc-detected-thread-requirements.test.cjs.
No assertion in the included regression files is removed or skipped.

The classic worker runtime uses a separate npm alias pinned to source Three.js
0.129.0. The existing additive viewer remains on Three.js 0.185.1. The asset build
reproduces the worker vendor scripts and their license from the locked package.

Local validation: Release build zero warnings/errors; 137 existing browser-module
tests pass. The 375-test source suite initially passed 361 tests; the 14 missing
fixture/vendor failures were corrected and all 25 tests in the affected files
passed on rerun. Required CI runs the entire source suite again before merge.

The source receipt state machine is also ported with all five source tests and
Development-only registration. It retains atomic whole-set claims, reservation
rollback, non-expiring pending tombstones, and `IsSharedDistributedAtomic=false`.
The latest source availability predicate is covered by eleven cases, ready for
the pending CNC route. No production distributed receipt store is introduced.
The integrated .NET suite passes all 1,618 tests.

The following source commits touch only files included in this slice. Their final
behavior is validated against the pinned source snapshot before marking complete.
Commits that also change pages, C# browser tests, or other contracts remain open.

- `9fd03d9bad9432270e4a47b2e5dfe11269122aa3`
- `7b2ab11265001b46e01e6d053d40e91c6e39fbb8`
- `0677c2c022ec32165da6f1f641e0149a65030c71`
- `481f951a588401275a064a4c7b73cdfacc534a9a`
- `0b6729d669d6012c6f5d7d19dde25f2b6b143a70`
- `a7b4991c2c0de3dcebedbb9ac0fef9ec20500ee0`
- `4433c7153b533142c1b135ecaf2a2ebf4c689ff0`
- `cad1d3f6e48784e42e0183b01fb531c7f4c08259`
- `619dc1b8a77807e230c0915b63ba64a20b74e62b`
- `f2293da4ce4e2be8853ee135cd69bc342e05e381`
- `010b2c41be5c156b80fc41ea06dc3304376ba39e`
- `5ecce2c9e733dabcb1fc864d7a35965475ed36aa`
- `3be593357f14c93505b0c609b5dff2d0942b38a6`
- `21b9778d5bf906ca4e8b144412c648e49a57ec06`
- `8b0e090b60499f1204fa7ad4745dd1547732e8ec`
- `672a258a06f505bc2e43c2d709c06dd737d05c8f`
- `03b56290efe443a93fb646726926ed282e08e267`
- `d270f3fb1e2fb0fbf66f8bc5fad14f50d2746cad`
- `dddc1b51c046dada1f681a47e86f59eb2d00ef91`
- `89ea801e8ba850bfcba0f8ceb42574688c9f2b43`
- `20ea0ddc0ee51be13a4fd3969b197a82ea975551`
- `6c5a46505deb9dc23aea21198e98ce5bc6ab6d2c`
- `30912eee8850dc3e892ea85669d6c8bd387cc500`
- `aa53f78aaf49513655f9545bd20651ae49131e9e`
- `8bfb6cb295740c6497bee8d3fe45dce1fd158402`
- `8744175e55c0c1b275ca64f498332caa791c0533`
- `01f4943950006bd51b0cfb8aff07544c86ac36ab`
- `b29b4d6b0b25a5f1e13e4604540f8f508d771e12`
- `afa4bd595dc9e71e8848ce73e1801e0d93305d4a`
- `964ae6b47a9a31c02bba9f2b084a4e14c8767175`
- `7da39ab2c3a6e4160442c04b2dc5e9e75d419d95`
