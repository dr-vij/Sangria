# SangriaMesh Core Review (Updated)

Date: 2026-03-08  
Scope: `Packages/com.propellerheadvij.sangria/SangriaMesh/Scripts/SangriaMesh` and `Scripts/Generators` in `namespace SangriaMesh` only.  
Explicitly excluded: `Scripts-legacy`, `ViJMeshTools`, and any legacy cutter/tessellation code paths.

## Executive Summary

The core is in a much better state than in the previous pass.  
The two most dangerous correctness risks (stale attribute handles and generation reset in dense allocation) are fixed.  
The four high-priority items from the last report are also now addressed in code:

1. Attribute/resource schema changes now update `AttributeVersion`.
2. Dense topology allocation now clears custom attribute ranges explicitly.
3. `PrimitiveStorage` now has garbage tracking and compaction behavior.
4. Vertex/point deletion no longer depends on global full scans; adjacency caches were introduced.

For planned expansion (interactive mesh editing, vertex deletion modes, local topology tools), this is a meaningful step forward.

The remaining risks are mostly about performance predictability and API robustness rather than immediate correctness bugs.

## What Was Fixed Since The Last Revision

### 1. Handle safety for dynamic attribute schemas

Previously, a removed attribute could leave behind a stale handle that accidentally pointed to another column after internal swap-back.  
Now, `AttributeStore` checks `AttributeId` in addition to column index and type hash when accessing through a handle.

This closes a silent corruption class that was especially dangerous in long editor sessions with dynamic attribute registration.

Relevant file:
- `Scripts/SangriaMesh/AttributeStore.cs`

### 2. Generation integrity in dense allocation

Previously, `AllocateDenseRange` could reset generations to `1`, potentially re-validating old handles after clear/rebuild cycles.  
Now generation is only initialized for zeroed slots, preserving monotonic generation semantics.

Relevant file:
- `Scripts/SangriaMesh/SparseHandleSet.cs`

### 3. Versioning contract for schema/resource mutations

Previously, `AttributeVersion` mostly reflected value writes, but not schema-level changes (add/remove attribute, set/remove resource).  
Now successful structural mutations increment `AttributeVersion`, which makes future cache invalidation and incremental compile strategies safer.

Relevant file:
- `Scripts/SangriaMesh/NativeDetail.cs`

### 4. Explicit attribute range clearing on dense rebuild

`AllocateDenseTopologyUnchecked` now clears point/vertex/primitive attribute ranges for newly alive elements, preventing stale payload leakage between rebuilds.

Relevant files:
- `Scripts/SangriaMesh/AttributeStore.cs`
- `Scripts/SangriaMesh/NativeDetail.cs`

### 5. Primitive storage reclaim strategy

`PrimitiveStorage` now tracks garbage capacity and compacts data when fragmentation crosses a threshold.  
It also clears record/data arrays fully on `Clear()`.

This materially improves long-session behavior under repeated topology mutations.

Relevant file:
- `Scripts/SangriaMesh/PrimitiveStorage.cs`

### 6. Adjacency-aware deletion flow

`NativeDetail` now maintains `Point -> Vertices` and `Vertex -> Primitives` maps.  
Deletion paths (`RemovePoint`, `RemoveVertex`) use these maps, with lazy rebuild when adjacency is marked dirty.

This removes the previous always-global scans and establishes a better base for edit tools.

Relevant file:
- `Scripts/SangriaMesh/NativeDetail.cs`

## Current State Of The Core (After Fixes)

### What is now strong

The data model remains clean and practical: points, vertices, primitives, typed attributes, and compiled dense snapshots.  
With the recent fixes, lifecycle guarantees are significantly stronger, and mutation paths are more consistent with future editing workloads.

The sphere generator and triangle fast path still provide a solid high-throughput route for runtime generation.

### What still deserves attention

The core is now safer, but not yet fully optimized for large interactive edit sessions.  
The remaining work is more about predictability under load than correctness blockers.

## Open Findings (Current)

### High: synchronous compaction can create frame-time spikes

`PrimitiveStorage` compaction is currently in-band and threshold-triggered.  
That is simple and valid, but if a large mesh crosses the threshold during interactive editing, compaction cost is paid immediately in that mutation call.

Why this matters:
- You avoid unbounded memory growth, but you may introduce occasional latency spikes.

Suggested next step:
- Add an explicit `Compact()` API and/or deferred compaction policy (for example, run only at controlled sync points).

### High: adjacency map updates are robust but still allocation/work heavy in hot mutation loops

The new adjacency model is a clear improvement over global scans.  
However, key-level removals currently rebuild value lists for that key (`remove + re-add kept values` pattern), which can still be expensive for high-valence vertices.

Why this matters:
- Better asymptotics than full scans, but frequent local edits on dense hubs can still become costly.

Suggested next step:
- Introduce a lighter-weight adjacency entry structure with direct remove support or pooled scratch buffers to reduce per-mutation work.

### Medium: compile path is still full snapshot rebuild

`Compile()` still rebuilds packed topology + attributes + resources every call.  
With the new versioning and safer contracts, the codebase is now better prepared for incremental compilation, but it is not implemented yet.

Why this matters:
- In node graphs with micro-edits, cost remains proportional to total mesh size.

Suggested next step:
- Track dirty ranges per domain and patch only modified spans in compiled buffers.

### Medium: polygon conversion path remains managed and non-linear

General polygon triangulation in `SangriaMeshUnityMeshExtensions` is still managed and relatively expensive compared to triangle-only conversion.

Why this matters:
- For mixed topology workloads, this can dominate conversion time and allocation pressure.

Suggested next step:
- Keep current path for correctness fallback, but add a faster native/Burst triangulation route for common polygon cases.

### Medium: `NativeCompiledDetail` ownership model is still easy to misuse

`NativeCompiledDetail` remains a mutable struct owning native memory.  
This works, but value-copy accidents can still lead to disposal mistakes in downstream usage.

Why this matters:
- It is a reliability footgun for external consumers of the API.

Suggested next step:
- Consider a safer ownership wrapper or stricter usage pattern documentation.

## Test Coverage Added For This Iteration

The following tests were added to cover the latest fixes:

- `AttributeVersionIncrementsForSchemaAndResourceMutations`
- `DenseRebuildClearsAllCustomAttributeDomains`
- `PrimitiveStorageCompactsAfterLargeGarbageAccumulation`
- `RemovingVertexAfterPrimitiveMutationKeepsAdjacencyConsistent`
- `RemovingPointWithMultipleVerticesRemovesAllIncidentPrimitives`
- `RemovingVertexAfterDensePopulateRebuildsAdjacencyCache`

Previously added critical tests are still relevant:

- `StaleAttributeHandleIsRejectedAfterRemoveAndReAdd`
- `PointHandleStaysInvalidAfterDenseRebuildPath`

## Practical Next Steps For Mesh Editing Expansion

If the immediate goal is to continue toward robust mesh editing features (vertex delete policies, collapses, local surgery), the most pragmatic sequence is:

1. Stabilize adjacency performance and compaction scheduling so mutation latency is predictable.
2. Add explicit edit-policy APIs (for example, remove vertex with strategy flags).
3. Introduce incremental compile for small local edits.
4. Keep polygon conversion as a separate optimization track so topology authoring and mesh conversion concerns do not block each other.

This keeps momentum on functionality while reducing the chance of regressions in large real scenes.

## Validation Note

This report is based on static code inspection and the added test set in source.  
Build/test execution was not run in this review pass.

