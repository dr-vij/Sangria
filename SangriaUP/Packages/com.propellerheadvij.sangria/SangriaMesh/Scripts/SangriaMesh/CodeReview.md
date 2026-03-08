# SangriaMesh Code Review (Deep Pass)

Date: 2026-03-08  
Scope: `Packages/com.propellerheadvij.sangria/SangriaMesh/Scripts/**` only.  
Excluded intentionally: `Scripts-legacy/**`, `Legacy/**`, and `ViJMeshTools` stack.

## 1. Executive Summary

This review confirms that the **new SangriaMesh core** is already a strong foundation for Unity-oriented runtime mesh workflows:

- clean domain model (`Point`, `Vertex`, `Primitive`)
- handle-based mutable topology (`SparseHandleSet` + generations)
- domain attribute system with typed accessors
- dense compiled snapshot (`NativeCompiledDetail`) for runtime consumption
- practical fast path for triangle-only conversion via Unity MeshData

At the same time, the current implementation still has several blockers for a robust MVP in production-heavy scenarios:

1. a high-risk polygon triangulation fallback bug (index write cursor safety)
2. fixed-capacity hash structures without explicit growth safeguards
3. ownership safety footguns due to mutable structs that own native memory
4. global attribute id registry limitations for deterministic or concurrent workflows
5. some residual architecture noise in `Scripts/Misc` (namespace drift / duplicates)

Core message: **the architecture is good, but robustness contracts are not finished yet**. The next MVP stage should focus on hardening and predictability before adding many new operators.

---

## 2. What Is Strong Today

### 2.1 Data Model and Layering

The split between mutable authoring (`NativeDetail`) and packed runtime snapshot (`NativeCompiledDetail`) is practical and production-aligned for Unity runtime.

Relevant files:

- `NativeDetail` root and partials
- `NativeCompiledDetail`
- `CompiledAttributeSet`, `CompiledResourceSet`

### 2.2 Handle Stability and Slot Reuse Model

`SparseHandleSet` generation-based handles are implemented with clear semantics:

- index reuse is guarded by generation increment
- dense allocation path preserves generation initialization rules
- explicit alive-bit scanning has efficient bitwise implementation

Relevant file:

- `SparseHandleSet.cs`

### 2.3 Attribute/Resource Extensibility

The typed attribute pipeline is well shaped for Burst-friendly access:

- `AttributeHandle<T>` and `NativeAttributeAccessor<T>` for editable layer
- `CompiledAttributeAccessor<T>` for packed layer
- consistent type hash checks

Relevant files:

- `AttributeStore.cs`
- `NativeAttributeAccessor.cs`
- `CompiledAttributeAccessor.cs`

### 2.4 Practical Runtime Throughput Route

For triangle-only topology, `FillUnityMeshTriangles` is significantly stronger than the generic path:

- direct MeshData API
- low abstraction overhead
- optional normals/uv streams

Relevant file:

- `SangriaMeshUnityMeshExtensions.cs`

### 2.5 Tests Coverage Baseline

There is meaningful tests coverage inside `Scripts/Tests` for:

- sparse compile remapping
- deletion policies
- adjacency consistency
- garbage collection behavior in primitive storage
- dense rebuild safety regressions

That is a good base for hardening work.

---

## 3. Findings (ordered by severity)

## 3.1 High — Polygon conversion can overrun preallocated index buffer in fallback path

### Where

- `SangriaMeshUnityMeshExtensions.cs`:
  - triangulation write inside ear clipping
  - fallback to fan triangulation on failure

### Why this is dangerous

In `FillUnityMeshInternal`, a fixed-size `triangles` array is preallocated as `(n-2)*3` per polygon primitive.  
`TryTriangulateEarClip(...)` writes triangles **incrementally** into that shared array. If ear clipping later fails (returns `false`), caller switches to fan fallback, but does not rollback `triangleWriteIndex` to the start of the current primitive.

If any triangle was already written before failure, fallback writes additional triangles for the same primitive and can exceed the preallocated range or corrupt subsequent primitive data.

### Risk profile

- Severity: high correctness and stability risk.
- Trigger probability: medium on malformed/self-intersecting or numerically unstable polygons.
- Runtime impact: potential out-of-range writes / exceptions in managed path.

### Recommendation

For each primitive in polygon path:

1. capture `int primitiveStartWrite = triangleWriteIndex`
2. call ear clipping
3. if ear clipping fails, set `triangleWriteIndex = primitiveStartWrite` before fan fallback

Additionally:

- add a defensive postcondition check that each primitive contributes exactly `(primitiveVertexCount - 2) * 3` indices
- add tests for partial ear-clip-fail scenarios

---

## 3.2 Medium — Hash maps rely on initial capacity without explicit growth policy

### Where

- `AttributeStore` uses `NativeParallelHashMap<int,int>` for id→column mapping
- `ResourceRegistry` uses `UnsafeParallelHashMap<int, ResourceEntry>`

### Why this matters

These containers are initialized with estimates (`estimatedAttributeCount`, `initialCapacity`) and inserts happen later. There is no explicit capacity growth policy in code around insertion points.

In real projects with dynamic schemas/resources, this can lead to insertion failures or runtime exceptions depending on allocator/container behavior under load.

### Recommendation

Before insertion (`RegisterAttribute`, `SetResource` new-id path):

- ensure map capacity can hold `Count + 1`
- implement deterministic doubling strategy
- include tests that intentionally exceed initial capacity

---

## 3.3 Medium — Ownership model is easy to misuse (native-memory-owning mutable structs)

### Where

- `NativeDetail`
- `NativeCompiledDetail`
- `CompiledAttributeSet`
- `CompiledResourceSet`

### Why this matters

All these are mutable `struct` types with `Dispose()`. Value copies can accidentally duplicate ownership handles to native memory. This is a common reliability hazard in Unity native-container ecosystems.

### Recommendation

MVP-safe options:

- provide a documented strict usage contract + analyzers/style checks
- add `IsCreated`/`IsDisposed` guard methods and fail-fast checks for public methods
- strongly consider introducing owner wrappers (reference type or explicit owner token patterns) for externally consumed compiled snapshots

---

## 3.4 Medium — Global `AttributeID` registry is not deterministic-safe for all usage modes

### Where

- `AttributeId.cs`

### Why this matters

`AttributeID` is process-global mutable state with runtime-assigned ids:

- non-thread-safe for concurrent registrations
- id assignment depends on registration order
- can hurt determinism when external systems serialize/assume stable numeric ids

### Recommendation

For MVP stability:

- split static built-in ids from runtime-registered custom ids
- enforce explicit deterministic registration phase (single-threaded)
- optionally provide string-key serialization mode for custom attributes

---

## 3.5 Low — Residual architecture noise inside `Scripts/Misc`

### Where

- duplicate `PageInfo` definitions
- mixed namespaces (`PropellerheadMesh`, `Legacy.Misc`, `SangriaMesh`) in current Scripts scope

### Why this matters

Not a direct runtime blocker, but increases cognitive and maintenance cost and blurs boundaries of “new core” vs transitional utility code.

### Recommendation

- collapse duplicate `PageInfo` into one canonical type
- move or rename misc legacy-style namespaces to avoid accidental coupling
- document intended support status of each misc utility

---

## 4. Gaps in Test Strategy

Current tests are useful, but MVP hardening needs targeted additions:

1. polygon triangulation stress tests with pathological input sets:
   - nearly collinear vertices
   - repeated vertices
   - near-zero area polygons
   - self-intersections (expected behavior should be explicit)
2. capacity growth tests for attribute/resource registries
3. ownership misuse tests:
   - copied compiled snapshots
   - double-dispose patterns (ensure predictable fail-fast)
4. long-session mutation tests:
   - repeated add/remove/compile loops with periodic `CollectGarbage`

---

## 5. Comparison to CGAL / Blender / Similar Ecosystems

This section compares **engineering profile**, not just feature count.

## 5.1 SangriaMesh vs CGAL

### SangriaMesh strengths

- Unity-native runtime integration (Burst + NativeCollections + MeshData path)
- lower integration friction for game runtime scenarios
- compact and understandable core abstraction

### CGAL strengths

- far stronger geometric robustness (exact predicates/kernels)
- mature algorithms for boolean ops, remeshing, repair, topology processing
- broader academic and production validation on edge cases

### Gap summary

SangriaMesh is currently optimized for practical runtime authoring/conversion, but does not yet match CGAL-level robustness contracts for difficult geometric operations.

## 5.2 SangriaMesh vs Blender (BMesh ecosystem)

### SangriaMesh strengths

- lean runtime footprint and data path for procedural generation in Unity
- simpler API surface for programmatic workflows

### Blender strengths

- very mature editing operators and topology manipulation tooling
- robust handling of many artist-facing edge cases
- deep ecosystem for mesh cleanup/repair/remesh workflows

### Gap summary

Blender/BMesh remains much richer for interactive editing semantics and operator coverage. SangriaMesh should prioritize a smaller but reliable subset for MVP.

## 5.3 Practical position

SangriaMesh should not attempt to “out-CGAL” or “out-Blender” in one step. The best product strategy is:

- own the Unity runtime-first niche
- build robust core contracts for that niche
- optionally bridge to heavy geometry backends for offline/high-quality operations

---

## 6. MVP Improvement Plan (Detailed)

## Stage A — Robustness Hardening (highest priority)

Goal: remove correctness hazards and stabilize API contracts.

1. Fix triangulation rollback bug in polygon conversion path.
2. Add container capacity growth guards in attribute/resource registries.
3. Add fail-fast disposal/ownership guards for compiled containers.
4. Define and document expected behavior on non-simple polygons.
5. Add targeted tests for all above.

Exit criteria:

- no known index write hazards in conversion path
- deterministic behavior under oversized dynamic schema/resource registrations
- explicit behavior and tests for invalid polygon classes

## Stage B — Performance Predictability

Goal: remove latency spikes and full-snapshot overhead where possible.

1. Add optional deferred compaction policy for `PrimitiveStorage`.
2. Introduce dirty-range tracking to enable incremental compile path.
3. Keep full compile as fallback; benchmark both.
4. Reduce managed allocations in generic polygon conversion path.

Exit criteria:

- mutation latency profile is bounded/predictable in long edit sessions
- compile time scales better with local edits

## Stage C — Editing Operators MVP

Goal: provide a robust minimal editing toolbox.

1. Add explicit topology operators:
   - edge split
   - edge collapse
   - edge/diagonal flip (where valid)
2. Define attribute propagation policies per operator.
3. Add topology validity checks as reusable validator utilities.

Exit criteria:

- stable minimal operator set with clear policies and tests
- no silent attribute corruption during edits

## Stage D — Advanced / Optional Hybrid

Goal: support high-robustness workflows without bloating runtime core.

1. Keep SangriaMesh core lean for runtime.
2. Add optional offline bridge for heavy operations (e.g., native plugin path using robust geometry backend).
3. Provide conversion contracts between compiled snapshot and offline processing format.

Exit criteria:

- clear split: fast runtime core vs robust offline toolchain

---

## 7. Recommended Backlog (Concrete Tickets)

1. `SMESH-001`: Ear-clip fallback rollback fix + regression tests.
2. `SMESH-002`: AttributeStore id-map growth safeguards.
3. `SMESH-003`: ResourceRegistry map growth safeguards.
4. `SMESH-004`: Ownership safety guardrail pass for compiled containers.
5. `SMESH-005`: Polygon validity policy doc + tests.
6. `SMESH-006`: PrimitiveStorage deferred compaction mode.
7. `SMESH-007`: Compile dirty-range prototype.
8. `SMESH-008`: Misc namespace/type cleanup (`PageInfo`, legacy misc names).

---

## 8. Final Assessment

SangriaMesh is already beyond prototype quality in architecture and runtime direction.  
The immediate bottleneck is not “missing big features,” but **robustness and contract hardening**.

If Stage A is completed first, the project can move into MVP expansion with materially lower regression risk and clearer positioning against larger ecosystems.

---

## Validation Note

This report is based on static code inspection in the declared scope and existing test sources.  
Build and test execution were not run as part of this review pass.
