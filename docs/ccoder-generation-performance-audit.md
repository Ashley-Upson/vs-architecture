# cCoder generation performance and redundancy audit

Audit baseline: Stage A plus development-only instrumentation commits `85f881e` and `5e20bf5`, branch `feature/decuplicate-node-option`, 18 July 2026. Stage B is paused. This document records the pre-cleanup evidence; implemented cleanup and after timings are appended only after parity gates pass.

## 1. Benchmark methodology

The reproducible harness is [`scripts/benchmark-generation.ps1`](../scripts/benchmark-generation.ps1). It uses one Release build, one warm-up, five measured executions, a new `dotnet` process for every execution, local NTFS outputs, unchanged cCoder source, identical recorded settings, and SHA-256 repeat checks. No process or Roslyn workspace is reused between runs. Within each process, the lightweight CLI workspace is cold; OS file-system and assembly caches are warm after the warm-up. The first run after the Release build is excluded.

Environment: Windows 10.0.19045 x64, .NET SDK 10.0.103, host 10.0.3. WMI hardware queries were denied in the execution sandbox, so CPU and physical-memory identity are not asserted. No concurrent benchmark load was used. Instrumented profiles were run separately from acceptance timings because primitive counters measurably add overhead.

Inputs:

- cCoder: `C:\Users\Ash\Documents\ccoder\ccoder.ContentManagement\src\cCoder.ContentManagement`
- StandardIo: `StandardIo.ArchitectureDiagram.sln`
- duplicated settings: `artifacts/routing-regression/effective-vsix-settings.json`
- deduplicated settings: `artifacts/node-duplication/real-project-deduplicated-settings.json`

All commands use `--renderer drawio` and an explicit output. `normal` requests no diagnostics. `diagnostic` uses `--diagnostics-output` and therefore includes JSON, annotated output, every focused output, and their writes. `strict` currently materializes the same diagnostic bundle and then returns exit code 1 when enforced findings exist.

There is no pre-cleanup JSON-only CLI path. JSON, annotation, and focused diagrams share one lazy factory. Consequently an honest “normal plus JSON only” baseline cannot be isolated; this coupling is itself a diagnostic design defect. It is not mislabeled below.

## 2. Controlled pre-cleanup timings

Times are minimum / median / maximum seconds across five measured fresh processes.

| Graph/mode | Normal | Diagnostic bundle | Strict |
| --- | ---: | ---: | ---: |
| StandardIo duplicated | 3.081 / **3.087** / 3.160 | 3.109 / **3.125** / 3.171 | 3.121 / **3.259** / 3.279 |
| StandardIo deduplicated | 3.182 / **3.210** / 3.225 | 3.282 / **3.322** / 3.378 | 3.231 / **3.320** / 3.446 |
| cCoder duplicated | 31.905 / **32.464** / 32.635 | 33.108 / **33.397** / 35.065 | 32.699 / **33.374** / 33.422 |
| cCoder deduplicated | 51.488 / **52.355** / 54.448 | 54.748 / **54.802** / 56.741 | 53.739 / **54.402** / 55.163 |

Every five-run group produced one XML hash. Strict and diagnostic control flow did not change the base diagram. The earlier 72.7/81.2-second observations were Debug-path measurements; controlled Release timings are the cleanup baseline.

Diagnostic bundle overhead is about 0.93 seconds duplicated and 2.45 seconds deduplicated at the medians. It is real but not dominant.

## 3. Phase timing tree

Development-only profiles record inclusive time, exclusive time, invocation count, aggregate input sizes, and layout/route revisions where the caller has them. The full JSON is under `artifacts/performance-audit/before/*profile-final.perf.json`.

Representative duplicated profile (instrumented wall time 43.4 seconds):

| Phase | Invocations | Inclusive ms | Interpretation |
| --- | ---: | ---: | --- |
| layout and routing | 1 | about 40,000 | dominant product cost |
| legacy route generation | 1 | about 39,700 | almost all layout/routing time |
| candidate construction and selection | 2 | about 19,000 | mainly regional scoring |
| regional optimisation | 2 | about 16,800 | global pair scoring within regions |
| repair passes | 1 | about 17,500 | 39 compile/validate pipelines |
| final validation, all repair paths | 39 | about 15,600 | accepted full confirmations dominate |
| semantic analysis | 1 | about 1,800 | includes three compilation requests |
| workspace acquisition | 1 | about 745 | cold lightweight workspace |
| normal serialization | 1 | about 167 | minor |
| file write | 1 | about 90 | minor |

Representative deduplicated profile (instrumented wall time 62.7 seconds):

| Phase | Invocations | Inclusive ms | Interpretation |
| --- | ---: | ---: | --- |
| layout and routing | 1 | about 56,000 | dominant product cost |
| legacy route generation | 1 | about 56,000 | almost all layout/routing time |
| repair passes | 1 | about 39,000 | dominant branch |
| traversal/junction compilation, all repair paths | 41 | about 40,400 | linear junction lookup hotspot |
| repair accepted-candidate confirmation | 14 | about 30,300 | whole-graph confirmations |
| candidate construction and selection | 4 | about 11,700 | second-largest branch |
| regional optimisation | 4 | about 6,000 | changes selected routes; not bypassable |
| semantic analysis | 1 | about 1,800 | minor relative to routing |
| normal serialization | 1 | about 259 | minor |
| file write | 1 | about 353 | minor |

Inclusive child totals overlap their parents and must not be summed as wall time. Invocation counts distinguish one expensive call from repeated calls.

## 4. Primitive-operation counts

| Operation | Duplicated | Deduplicated |
| --- | ---: | ---: |
| Roslyn compilation requests | 3 | 3 |
| syntax trees visited | 782 | 782 |
| semantic models requested | 782 | 782 |
| symbols inspected | 790 | 790 |
| render paths/clones | 1,094 | 180 canonical render nodes |
| logical routes | 1,072 | 294 |
| candidate routes created | 2,087 | 42,336 |
| candidate routes retained (mutable paths only) | 397 | 5,900 |
| candidate obstacle checks | 4,231 | 65,532 |
| candidate node/segment obstacle checks | 10,368,148 | 29,870,330 |
| candidate routes rejected | not material in profile top set | 19,826 |
| global score evaluations | 4,052 | 78,683 |
| global score route-pair evaluations | 24,208,239 | 2,662,333 |
| global score segment-pair evaluations | 121,610,275 | 68,172,113 |
| regional interaction checks | 2,296,224 | 344,568 |
| interaction pairs discovered | 429 | 10,944 |
| corridor models built | 39 | 43 |
| corridor segment observations | 36,771 | 33,592 |
| lane-allocation models built | 39 | 43 |
| traversal records compiled | 16,327 | 7,007 |
| traversal fallbacks | 249 | 3,777 |
| junction-allocated traversals | 419 | 4,988 |
| junction transition lookups | 878 | 8,990 |
| junction lookup candidate checks | **1,680,840** | **49,347,824** |
| normalizer invocations | 39 | 41 |
| full validator invocations | 39 | 41 |
| validation route-pair checks | 8,612,306 | 868,088 |
| validation segment-pair checks | 42,741,598 | 17,830,728 |
| node-route checks | 17,829,084 | 1,247,246 |
| repair candidates generated | 26 | 25 |
| repair candidates attempted | 24 | 24 |
| accepted whole-graph confirmations | 12 | 12 |
| complete regional route-set copies | 24 | 24 |
| XML metadata projections/lookups | 1,072 | 320 physical segments |
| diagnostic materializations in normal generation | **0** | **0** |

Selector work cannot currently be bypassed wholesale: duplicated selection changed 39 regional route choices and recorded 32 applied regional decisions; deduplicated selection changed 56 routes and recorded 56 applied decisions. Global/local selector invocations likewise reported 60 and 56 changed choices. Candidate and selector work is expensive but affects authoritative geometry.

## 5. Roslyn analysis

Each selected project requests its compilation three times: type discovery, service-registration discovery, and constructor dependency discovery. Roslyn returns cached compilation objects inside one workspace, so this is not three complete compilations; measured aggregate acquisition is approximately 0.53–0.59 seconds. The three semantic passes are distinct and should remain distinct. The acquisition itself and repeated project/document ordering can safely share one immutable per-project analysis context.

Syntax roots and semantic models are requested twice per syntax-bearing document: registration and dependency passes. Sharing roots/models is possible, but it has a broader lifetime/allocation tradeoff and is not the first cleanup target. No evidence supports merging semantically different passes.

## 6. Exposure graph

Duplicated mode creates 1,094 visible render clones and 1,072 render links. Deduplicated mode creates 180 visible render nodes and 294 links. No arbitrary multiplicity cap is appropriate. Exposure construction takes milliseconds, not tens of seconds, and its structures are consumed by placement/routing. It is necessary.

The clone ID suffix preserves the semantic node identity, so multiplicity is diagnosable without changing IDs. Path-list copying exists in recursive traversal, but it is not a measured hotspot.

## 7. Candidate and selection work

Candidate construction is broad, especially deduplicated mode: 42,336 created, 19,826 obstacle-rejected, and 5,900 retained across repeated layout revisions. Obstacle checks scan complete node rectangles and are a valid future placement-revision spatial-index target.

Global and regional selection share `GlobalCorridorPathSelector.Score`, which repeatedly evaluates route and segment pairs. Regional interaction discovery also scores route pairs, and final validation later evaluates similar geometry under different semantics. These computations overlap geometrically but are not currently interchangeable: selector scoring includes fan-out, capacity, envelope and local-cost terms; validation produces exact diagnostics and locations. A cache may share low-level segment relationships by route revision, but selector results cannot replace final validation.

Both selectors demonstrably change choices, so bypassing either complete mechanism is not supported by this graph.

## 8. Corridor, traversal and junction work

Corridor observation and lane allocation are repeated for every repair trial and accepted confirmation. They affect trial geometry and cannot simply be removed.

The clearest implementation accident is junction lookup in `EdgeTraversalCompiler.Map`: every adjacent corridor transition sorts and linearly scans every observed junction until it finds one containing both corridor IDs. Deduplicated mode performs 8,990 transition lookups but 49,347,824 candidate checks. A deterministic dictionary keyed by the unordered corridor pair can preserve the current first-by-junction-ID result while reducing lookup to O(1). This does not remove a candidate, traversal, validation, or fallback.

Traversal commonly falls back—3,777 of 7,007 compiled records across deduplicated repair pipelines—but 4,988 traversals also receive junction allocation. Therefore global traversal bypass is not justified. Indexing the existing mapping is the safe correction.

## 9. Validation and repair

A final whole-diagram validation remains required. Repair performs bounded regional trials followed by a whole-graph confirmation for an improving candidate. Twelve accepted confirmations occur in each mode; they affect the final route set and cannot be skipped.

Duplicated mode spends roughly 15.6 instrumented seconds across 39 validations and over 121 million selector segment-pair evaluations elsewhere. Deduplicated mode spends less in validation but far more in traversal lookup. Cacheable low-level route-pair facts should be keyed by both route revisions and validation policy. No unchanged-revision full validation has yet been proven safe to delete; this audit does not suppress it.

Regional trials copy the current route dictionary 24 times. Those copies are implementation overhead but small relative to scoring/traversal, and replacing them would deepen legacy code scheduled for Stage H.

## 10. XML construction and writing

Ten serialization-only repeats use the already prepared final layout and assert identical XML each time.

| Mode | Total for 10 | Average | XML tree construction | `ToString` |
| --- | ---: | ---: | ---: | ---: |
| duplicated | 923.4 ms | **92.3 ms** | 72.0 ms | 19.9 ms |
| deduplicated | 998.0 ms | **99.8 ms** | 54.0 ms | 44.1 ms |

Normal generation serialization is about 0.17–0.26 seconds; file writing about 0.09–0.35 seconds. Final documents are 3,015,166 and 12,455,618 UTF-8 bytes respectively. XML is not the 32–52 second cause.

The serializer performs linear path/region decision searches per physical segment. Prepared dictionaries are safe, but the maximum possible saving is small and should follow the routing hotspot.

## 11. Redundancy graph

```text
candidate construction
  -> obstacle segment/node relationships

regional interaction discovery
  -> route/route and segment/segment relationships

global/regional scoring
  -> route/route and segment/segment relationships

validation
  -> route/route and segment/segment relationships plus diagnostic locations

repair trial scoring
  -> corridor -> traversal -> normalization -> validation
```

The geometric predicates overlap, but their consumers and scoring policies differ. Reusable facts belong below those policies and must be revision-keyed.

```text
route revision R
  -> corridor observation
  -> lane allocation
  -> traversal mapping
       -> transition pair
       -> repeated scan of the same immutable junction collection
```

The final scan is semantically identical for the same corridor observation and is pure redundant lookup work. It should disappear, not be cached per route.

## 12. Classification

| Work | Classification | Decision |
| --- | --- | --- |
| semantic type/registration/dependency passes | Necessary | retain distinct passes |
| three compilation acquisitions | Repeated unchanged | reuse one per project |
| exposure/canonical graph creation | Necessary | retain |
| candidate generation and obstacle rejection | Necessary but over-broad | retain now; future placement-revision spatial index |
| global/regional selection | Necessary | changes selected routes |
| corridor/lane rebuild per changed trial | Necessary | retain |
| transition-to-junction linear scan | Implementation accident | replace with deterministic pair index |
| traversal fallback construction | Necessary | fallback protects authoritative geometry |
| accepted repair confirmations | Necessary | changes final routes |
| repeated low-level route-pair predicates | Necessary but over-broad | future revision-keyed cache, not this first cleanup |
| diagnostic bundle in normal generation | Not executed | materializations are zero |
| focused outputs coupled to JSON request | Diagnostic-only coupling | split laziness if justified after dominant fixes |
| XML decision linear searches | Implementation accident, minor | prepare dictionaries after routing hotspot |

## 13. Proposed removals and corrections

1. Build one deterministic junction-pair index per immutable corridor observation and replace 49.3 million deduplicated candidate checks.
2. Acquire one Roslyn compilation per project and share it across the three semantically distinct passes.
3. Prepare serializer decision/evaluation dictionaries once per final layout if measurement still justifies it.
4. Consider splitting JSON from focused diagnostic materialization; this improves optional-mode clarity, not normal generation.

No selector, repair, validation, corridor, traversal, route, or candidate family is approved for deletion by the current evidence. No dead active type has been proven. Git history remains the source for already removed legacy code.

## 14. Implemented removals

None at this audit commit. Cleanup begins only after this document is committed.

## 15. Remaining Stage B boundary

Stage B remains paused. The achievable legacy target from current evidence is a substantial deduplicated improvement by removing the 49.3-million-check junction scan, plus a smaller analysis saving. Duplicated mode is dominated by selection and validation that demonstrably affect geometry, so an under-15-second duplicated target still belongs to the later layer-band replacement rather than this low-risk legacy cleanup.
