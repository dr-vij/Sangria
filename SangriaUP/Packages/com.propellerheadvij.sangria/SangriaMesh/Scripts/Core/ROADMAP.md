# SangriaMesh Core Roadmap (Runtime-First, Best-in-Class)

Updated: 2026-03-08

This roadmap defines how SangriaMesh Core evolves from a strong Unity-native foundation into a best-in-class mesh processing stack for runtime and tool pipelines.

Primary strategy:
- Win on Unity runtime throughput and predictability.
- Reach production-grade robustness for topology and geometry operations.
- Add advanced operator coverage without sacrificing Burst-friendly performance.
- Bridge to heavyweight offline geometry where exactness is mandatory.

This is not a generic wish list. Each phase has concrete engineering work, validation gates, and measurable success criteria.

## 1) Product Positioning

SangriaMesh should outperform alternatives in the area where game teams feel pain most:
- Real-time procedural or editable meshes in Unity.
- Low-latency compile-to-render path.
- Stable, deterministic behavior under long mutation sessions.
- Clear ownership and safety contracts for native memory.

It does not need to replicate all of CGAL, Blender, or Houdini in one core runtime package. Instead:
- Core runtime stays lean and fast.
- Advanced exact/offline workflows are integrated through optional bridges.

## 2) Engineering North Stars

### NS-1: Robustness
- No known correctness hazards in topology edits or triangulation.
- Strict API contracts for disposal, ownership, and invalid handles.
- Deterministic output contracts where required for pipeline reproducibility.

### NS-2: Performance
- Stable frame-time behavior under repeated add/remove/edit cycles.
- Incremental compile paths for local edits.
- Triangle fast-path remains near-minimal overhead against raw MeshData usage.

### NS-3: Operator Quality
- Reliable MVP topology operators with explicit attribute propagation rules.
- Consistent validity checks and fail-fast errors for invalid operations.
- Scalable API shape for future node/operator expansion.

### NS-4: Integration
- Seamless Unity runtime usage.
- Optional offline high-robustness bridge for difficult geometry operations.
- Clean migration path from legacy stack toward new core contracts.

## 3) Competitive Benchmark Matrix (Target State)

### Against CGAL
- Keep CGAL-level exactness as an offline benchmark for selected operations.
- Target: runtime speed and integration simplicity where CGAL is costly to embed.
- Bridge strategy: optional native backend for exact booleans/repair/remesh.

### Against Blender/BMesh
- Match reliability of core topology edits for programmatic workflows.
- Target: fewer operator types but stronger runtime guarantees and lower overhead.
- Focus: non-interactive, deterministic runtime editing rather than DCC UI parity.

### Against Houdini
- Align with attribute-centric domain model (point/vertex/primitive/detail).
- Target: lower runtime integration cost in Unity and tighter frame-time control.
- Add procedural operator graph readiness through clean primitive and attribute APIs.

## 4) Phase Roadmap

## Phase A - Contracts and Safety Hardening (Highest Priority)

Goal: eliminate ownership/correctness hazards and make behavior explicit.

### A1. Ownership and disposal contract pass
- Introduce consistent `ThrowIfDisposed()` (or equivalent guard) across public mutable APIs.
- Audit all native-memory-owning structs (`NativeDetail`, `NativeCompiledDetail`, `CompiledAttributeSet`, `CompiledResourceSet`, `AttributeStore`, `ResourceRegistry`, `PrimitiveStorage`, `SparseHandleSet`) for copy safety.
- Define allowed ownership patterns in API docs:
  - single owner
  - pass-by-ref for mutating workflows
  - forbidden copy-and-dispose patterns
- Add defensive runtime checks in debug/development configurations.

Deliverables:
- Ownership contract document.
- Guard implementation pass.
- Regression tests for copied struct misuse patterns.

Success criteria:
- No silent undefined behavior from common misuse scenarios.
- Misuse fails fast with actionable errors.

### A2. Deterministic compile contracts for resources and schema
- Guarantee deterministic ordering in compiled resources/descriptors.
- Define deterministic registration phase for custom attribute IDs.
- Add tests that compare binary-equivalent compile outputs across runs.

Deliverables:
- Deterministic descriptor ordering policy.
- Test suite for repeatable compile outputs.

Success criteria:
- Identical inputs produce identical packed outputs (within declared contract scope).

### A3. Polygon conversion robustness envelope
- Keep ear clipping + fan fallback, but harden edge-case handling and diagnostics.
- Document behavior classes:
  - simple polygon
  - near-degenerate polygon
  - self-intersecting polygon
- Add stress tests with pathological inputs and expected outcomes.

Deliverables:
- Policy doc for polygon validity.
- Expanded triangulation stress tests and fixtures.

Success criteria:
- No write-count mismatches or index corruption in fallback paths.
- Explicit expected behavior on non-simple input.

Exit gate for Phase A:
- Core contracts are explicit, tested, and safe for extended sessions.

## Phase B - Predictable Performance and Incremental Compile

Goal: remove full-snapshot cost as default behavior and bound latency spikes.

### B1. Dirty-range and dependency tracking
- Track changed regions for:
  - vertex-to-point mapping
  - primitive topology ranges
  - attribute columns by domain
  - resources by ID
- Build minimal invalidation graph for compile subcomponents.

Deliverables:
- Dirty state subsystem with clear APIs.
- Instrumentation counters for changed elements and compile scope.

Success criteria:
- Local edits trigger local repack where possible.
- Full compile remains fallback path, not default for micro-edits.

### B2. Incremental compile pipeline
- Add partial rebuild stages:
  - topology offsets/indices partial rebuild
  - attribute chunk repack for touched ranges
  - resource subset repack
- Preserve output layout compatibility contracts.

Deliverables:
- Incremental compile implementation.
- Toggle for full vs incremental mode.
- Benchmarks for edit-size scaling.

Success criteria:
- Significant compile-time reduction for sparse/local edits.
- No correctness regressions compared to full compile.

### B3. Primitive storage compaction policy
- Add configurable compaction modes:
  - immediate
  - threshold-based
  - deferred/manual
- Expose metrics (garbage ratio, compaction cost, amortized mutation cost).

Deliverables:
- Compaction policy API.
- Performance tests under long random mutation sessions.

Success criteria:
- Mutation latency remains stable over long sessions.
- Compaction behavior is predictable and tunable.

Exit gate for Phase B:
- Compile and mutation costs are predictable under production-like workloads.

## Phase C - Topology Operator MVP (Quality Before Quantity)

Goal: deliver a minimal but production-solid operator set.

### C1. Add explicit edge representation
- Introduce edge or half-edge layer compatible with existing point/vertex/primitive model.
- Keep memory footprint and Burst constraints explicit.
- Define conversion/mapping between existing primitive storage and new edge layer.

Deliverables:
- Edge/half-edge core data structures.
- Consistency checks for adjacency and manifold assumptions.

Success criteria:
- Edge queries and edge-local edits are first-class and reliable.

### C2. MVP operators
- Implement and validate:
  - edge split
  - edge collapse
  - edge/diagonal flip (where valid)
  - optional weld/merge primitives for practical cleanup
- Each operator includes:
  - precondition validation
  - attribute propagation policy hooks
  - deterministic failure mode

Deliverables:
- Operator APIs with policy objects.
- Comprehensive unit tests and randomized topology fuzz tests.

Success criteria:
- Operators preserve topology/attribute invariants under declared conditions.

### C3. Validator toolkit
- Add reusable validators:
  - dangling references
  - invalid primitive lengths
  - broken adjacency
  - non-manifold detection (at least baseline)
- Integrate validators into debug/dev pipeline and CI tests.

Deliverables:
- `Validate*` API surface and diagnostics.
- Automated invariant checks in test suite.

Success criteria:
- Fast detection of corruption sources and reproducible failure reports.

Exit gate for Phase C:
- SangriaMesh has a trustworthy editing kernel for node/operator expansion.

## Phase D - Advanced Geometry and Hybrid Offline Bridge

Goal: exceed runtime-only limitations for difficult geometry classes.

### D1. Offline robust operation bridge
- Design optional bridge for heavy operations:
  - robust boolean
  - remesh/repair
  - hole filling and cleanup
- Define conversion contracts:
  - from `NativeCompiledDetail` to bridge format
  - from bridge output back to `NativeDetail`/compiled form

Deliverables:
- Bridge architecture and adapter layer.
- Deterministic conversion tests and geometry fidelity tests.

Success criteria:
- Hard geometry cases can be delegated without bloating runtime core.

### D2. Quality tiers
- Expose operation tiers:
  - `FastRuntime`
  - `Balanced`
  - `RobustOffline`
- Let graph/tooling select quality tier per operation chain.

Deliverables:
- Tier selection API and documentation.
- Benchmark + quality comparison reports.

Success criteria:
- Teams can choose speed vs robustness intentionally, operation by operation.

Exit gate for Phase D:
- SangriaMesh supports both high-throughput runtime workflows and high-robustness offline workflows.

## Phase E - Ecosystem and Developer Experience

Goal: make the core easy to adopt, verify, and scale.

### E1. Documentation system
- Keep architecture, contracts, and operator policies synchronized with implementation.
- Add practical recipes:
  - procedural generation
  - runtime edits with incremental compile
  - fallback to robust offline operations

### E2. Benchmark and quality dashboard
- Establish continuous benchmark suite:
  - compile latency
  - mutation throughput
  - conversion throughput
  - memory footprint
- Include geometry stress corpus with known pathological cases.

### E3. Migration path from legacy stack
- Define adapter and deprecation strategy for legacy cutter/tessellation pipeline.
- Set milestone dates for parallel support, migration, and final retirement.

Exit gate for Phase E:
- Core is not only fast and robust, but maintainable and adoption-ready across teams.

## 5) Program-Level Milestones and KPIs

### Milestone M1 (after Phase A)
- API safety contracts enforced.
- Determinism policy landed.
- No known high-severity correctness risks.

### Milestone M2 (after Phase B)
- Incremental compile available and benchmarked.
- Mutation and compile latency budgets met on representative workloads.

### Milestone M3 (after Phase C)
- MVP operators production-ready.
- Topology/attribute invariants continuously validated.

### Milestone M4 (after Phase D/E)
- Hybrid runtime+offline strategy operational.
- Public benchmark evidence of competitive leadership in Unity-native workflows.

Suggested KPI set:
- P50/P95 compile time by edit size.
- P50/P95 topology mutation time by operator type.
- Memory growth and fragmentation under long sessions.
- Invalid-topology detection latency and diagnostic coverage.
- Determinism pass rate in repeated-run tests.

## 6) Risk Register and Mitigations

### Risk R1: Feature creep before contract stability
- Mitigation: no expansion beyond Phase A gate until safety/determinism pass is complete.

### Risk R2: Incremental pipeline complexity
- Mitigation: keep full compile as oracle path; differential tests compare incremental vs full outputs.

### Risk R3: Edge model introduces memory/perf regressions
- Mitigation: benchmark every operator path; support compile-time toggles for edge features if needed.

### Risk R4: Offline bridge adds integration complexity
- Mitigation: isolate bridge in optional package and maintain strict format contracts.

## 7) Immediate Execution Backlog (Detailed)

1. `SMESH-A01` Ownership/Dispose Audit
- Add disposed guards on all public entry points that touch native containers.
- Add explicit docs for value-copy pitfalls and approved usage patterns.
- Create misuse tests for copied structs and repeated dispose.

2. `SMESH-A02` Deterministic Resource Packing
- Sort resource IDs during compile packing.
- Lock descriptor order contract and test for reproducibility.

3. `SMESH-A03` Attribute Registry Determinism
- Reserve built-in IDs in fixed range.
- Add explicit custom registration phase API.
- Add tests that registration order cannot silently break serialization contracts.

4. `SMESH-B01` Dirty-State Infrastructure
- Add per-domain dirty trackers and compile invalidation counters.
- Introduce debug metrics endpoints.

5. `SMESH-B02` Incremental Compile Prototype
- Implement topology-only incremental path first.
- Add differential validator against full compile.

6. `SMESH-C01` Edge/HalfEdge MVP Design
- Finalize data layout, memory budget, and mutation update rules.
- Implement baseline edge queries and validator hooks.

7. `SMESH-C02` Operator MVP Set
- Implement split/collapse/flip with attribute propagation strategies.
- Add operator-focused fuzz tests and corpus-based regression cases.

8. `SMESH-D01` Offline Robust Bridge RFC
- Define supported operations and format mapping.
- Evaluate optional backend integrations with legal/licensing constraints.

## 8) Reference Sources

CGAL:
- Surface Mesh: https://doc.cgal.org/latest/Surface_mesh/index.html
- Polygon Mesh Processing: https://doc.cgal.org/latest/Polygon_mesh_processing/index.html
- Exact Predicates Exact Constructions Kernel: https://doc.cgal.org/latest/Kernel_23/classCGAL_1_1Exact__predicates__exact__constructions__kernel.html
- Licensing: https://www.cgal.org/license.html

Blender:
- BMesh API: https://docs.blender.org/api/master/bmesh.html
- BMesh Operators: https://docs.blender.org/api/current/bmesh.ops.html
- Mesh Boolean Node (Manual): https://docs.blender.org/manual/en/latest/modeling/geometry_nodes/mesh/operations/mesh_boolean.html
- Licensing: https://www.blender.org/about/license/

Houdini:
- Half-Edges in VEX: https://www.sidefx.com/docs/houdini/vex/halfedges.html
- Boolean SOP: https://www.sidefx.com/docs/houdini/nodes/sop/boolean
- Geometry Attributes Model: https://www.sidefx.com/docs/houdini/model/attributes
- Product/License Compare: https://www.sidefx.com/products/compare/
