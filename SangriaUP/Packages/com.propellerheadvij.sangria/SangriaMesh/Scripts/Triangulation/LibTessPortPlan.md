# LibTessDotNet Port Plan for Sangria Mesh

## Goal

Port the robust polygon tessellation pipeline from Sangria's embedded `ThirdParty/LibTessDotNet` into Sangria Mesh Core in a form that is:

- zero managed-allocation in the hot path
- Burst-compatible
- job-schedulable
- based on native collections / unmanaged scratch memory
- able to emit directly into Sangria's native point/vertex/primitive topology

The target is not a 1:1 source translation. The target is a Sangria-native triangulation module that preserves LibTess behavior where it matters and replaces its managed object model with dense indexed workspace data.

## What LibTessDotNet Actually Does

The current flow is split across these files:

- `Tess.cs`
  - `AddContourInternal(...)` builds contour edges and windings.
  - `ProjectPolygon()` computes the sweep plane projection.
  - `TessellateInterior()` triangulates monotone regions.
  - `OutputPolymesh(...)` emits final vertex/index buffers.
- `Sweep.cs`
  - `ComputeInterior()` performs the sweep-line partition into monotone regions.
  - `InitPriorityQ()`, `InitEdgeDict()`, `SweepEvent(...)`, `CheckForIntersect(...)`, `AddRightEdges(...)`, `ConnectLeftVertex(...)` are the heart of the algorithm.
- `Mesh.cs` and `MeshUtils.cs`
  - implement the mutable quad-edge / half-edge-like topology used during sweep and region splitting.
- `Geom.cs`
  - contains the robust predicates and interpolation helpers that make the algorithm stable.
- `PriorityQueue.cs`, `PriorityHeap.cs`, `Dict.cs`
  - provide the event queue and active-region ordering structures.

In algorithm terms, LibTess does this:

1. Build contour topology.
2. Compute a stable 2D projection.
3. Sweep from left to right and partition the arrangement into monotone regions.
4. Triangulate each monotone region.
5. Optionally group triangles into larger polygons / connected polygon output.

For Sangria, step 5 is optional and should not be part of the first port milestone.

## Recommended Port Strategy

### Port the algorithm, not the object graph

Do not port:

- `class`-based pooled objects
- `object Data`
- callback-heavy output model
- managed arrays as the main working storage
- per-node heap allocations

Do port:

- the geometric predicates from `Geom.cs`
- the monotone decomposition logic from `Sweep.cs`
- the monotone triangulation logic from `TessellateMonoRegion(...)`
- the winding-rule behavior
- the degeneracy handling rules

### Keep the sweep workspace separate from `NativeDetail`

Do not perform the sweep by incrementally calling:

- `AddPoint(...)`
- `AddVertex(...)`
- `AddPrimitive(...)`

on `NativeDetail`.

That path would repeatedly touch adjacency caches, sparse-handle logic, and variable-length primitive mutation. It would be much slower than necessary.

Instead:

1. Run triangulation in a dedicated unmanaged scratch workspace.
2. Produce a dense triangle result.
3. Emit once into `NativeDetail` using Sangria's dense topology fast path.

This fits the existing Sangria design better because:

- `NativeDetail.AllocateDenseTopologyUnchecked(...)` already exists.
- `PrimitiveStorage.PrepareDenseTriangleRecords(...)` already supports dense triangle layout.
- `NativeDetail.Compile(...)` already has a triangle-only fast path.

## Best-Fit Sangria Architecture

### Public API shape

The triangulator should accept generic contour input, not only a `NativeDetail`.

Recommended public shape:

```csharp
public struct TriangulationOptions
{
    public WindingRule WindingRule;
    public bool ForceCounterClockwiseContours;
    public bool OutputBoundaryOnly;
    public bool StrictNoResize;
    public bool UseDoublePredicates;
}

public struct NativeContourSet
{
    public NativeArray<float3>.ReadOnly Positions;
    public NativeArray<int>.ReadOnly ContourOffsets;
    public NativeArray<int>.ReadOnly ContourPointIndices;
}

public struct TriangulationScratch : IDisposable
{
    // Reusable native workspace. Pass by ref only.
}

public static class SangriaTriangulator
{
    public static CoreResult TriangulateContours(
        in NativeContourSet contours,
        ref NativeDetail output,
        ref TriangulationScratch scratch,
        in TriangulationOptions options);
}
```

Notes:

- `output` should be empty for v1. Append mode can come later.
- `TriangulationScratch` should be reusable across calls and cleared, not recreated.
- If `TriangulationScratch` is public, document very clearly that it must be passed by `ref` and not copied by value.

### Internal pipeline

Recommended internal phases:

1. Input adaptation
2. Projection and orientation normalization
3. Sweep-line monotone partition
4. Monotone triangulation
5. Dense triangle emission into Sangria topology
6. Optional attribute interpolation

## Data Layout Plan

### 1. Sweep workspace

Create an indexed unmanaged workspace rather than pointer-linked objects.

Suggested records:

| LibTess concept | Sangria port shape | Notes |
| --- | --- | --- |
| `MeshUtils.Vertex` | `SweepVertex` | `float3 position`, `double2 st` or `float2 st`, `int anEdge`, `int pqHandle`, flags |
| `MeshUtils.Edge` | `SweepHalfEdge` | `int sym`, `int onext`, `int lnext`, `int org`, `int lface`, `int activeRegion`, `short winding` |
| `MeshUtils.Face` | `SweepFace` | `int anEdge`, flags, output id |
| `ActiveRegion` | `SweepRegion` | `int upperEdge`, `int nodeUp`, `int windingNumber`, flags |
| pooled nodes | free-list indices | no GC, no `IPool` |

Use integer handles everywhere. Avoid managed references entirely.

### 2. Native collection choices

Recommended split:

- Public boundaries: `NativeArray`, `NativeSlice`, `NativeList`
- Internal workspace: `UnsafeList<T>` where nesting or custom layout makes `Native*` awkward
- Output staging: `NativeList<int>`, `NativeList<float3>`, or raw pointers into pre-sized arrays

Reasoning:

- Unity recommends `Native*` collections by default.
- `Unsafe*` is justified for nested or custom internal layouts.
- Unity documents that with safety checks disabled, `Native` and `Unsafe` do not have a significant performance difference because most `Native` collections wrap the `Unsafe` versions.

### 3. Capacity model

To keep the runtime path allocation-free, the triangulator needs explicit capacity rules.

Recommended behavior:

- `TriangulationScratch.EnsureCapacity(...)` on setup / warmup
- `TriangulationScratch.Reset()` per call
- `StrictNoResize` option for production hot paths
- if capacity is insufficient in strict mode, return `CoreResult.OutOfMemory` or `CoreResult.CapacityExceeded`

Suggested initial capacity estimates:

- vertices: input contour vertex count
- edge pairs: input contour vertex count
- faces: contour count + 1
- active regions: input contour vertex count + sentinels
- heap handles: input contour vertex count + expected split/intersection growth

Intersections can create additional vertices and edges, so the API must either:

- allow workspace growth in non-strict mode, or
- require callers to overprovision scratch for worst-case data.

## Algorithm Mapping

### Projection and orientation

Port from:

- `Tess.ComputeNormal(...)`
- `Tess.CheckOrientation()`
- `Tess.ProjectPolygon()`

Recommendation:

- keep the dominant-axis projection strategy first
- preserve the contour-orientation correction logic
- use `double` for normal/projection math if robustness matters more than raw throughput

Do not start with true 3D projected sweep-space unless profiling or quality testing proves it necessary.

### Geometric predicates

Port `Geom.cs` early and keep it close to the original logic:

- `VertLeq`
- `EdgeEval`
- `EdgeSign`
- `TransEval`
- `TransSign`
- `EdgeIntersect`
- `Interpolate`
- `IsWindingInside`

Recommendation:

- keep these in a dedicated static Burst-compatible utility type
- prefer direct index-based overloads that read from `NativeArray`/`UnsafeList`
- avoid “cleaned up” rewrites until the port is validated against LibTess output

This code is correctness-critical. It is not the place to be clever.

### Quad-edge topology layer

Port from:

- `Mesh.cs`
- relevant parts of `MeshUtils.cs`

Recommendation:

- implement the minimum topology operations needed by the sweep
- keep the same conceptual operations:
  - `MakeEdge`
  - `Splice`
  - `Delete`
  - `AddEdgeVertex`
  - `SplitEdge`
  - `Connect`
  - `ZapFace`
- back them with indexed records and free lists

Do not keep generic pooled entities. Replace them with:

- dense lists
- free lists
- integer indices

### Event queue

Port from:

- `PriorityQueue.cs`
- `PriorityHeap.cs`

Recommendation:

- implement an indexed min-heap with removable handles
- keep handle-based remove/update semantics because the sweep changes event ordering when edges are split
- store vertex indices, not full vertex structs

This is a good place for a small custom container:

- `NativeMinHeap`
- `UnsafeMinHeap`
- or a triangulation-private heap implementation

### Active-region ordering

Port from:

- `Dict.cs`
- the `ActiveRegion` parts of `Sweep.cs`

Recommendation:

- first implementation: intrusive ordered list using indices
- optimization candidate: indexed balanced tree or skip-list if profiling shows dictionary search dominates

Why start with a list:

- it is lower-risk for correctness
- it stays very close to the original algorithm
- most region operations are local neighbor operations anyway

Why consider a later replacement:

- `Find(...)`-style searches can become expensive on large inputs
- Sangria's performance target is higher than “managed LibTess but with fewer allocations”

## Job System Plan

### What should be parallel

Good candidates for jobs:

1. contour adaptation and prefix sums
2. projection of 3D points to 2D sweep coordinates
3. optional contour orientation checks
4. output packing into dense point/vertex/triangle buffers
5. attribute interpolation for generated points, if expressed as independent ranges

### What should stay single-threaded

The core sweep should remain one `IJob` for the first implementation.

Reason:

- event processing is ordered
- active-region mutations are sequential
- edge intersections inject new future events
- correctness depends on maintaining a very tight set of local invariants

Trying to parallelize the actual sweep too early is likely to make the code slower and less reliable.

### Important Unity guidance for this design

From Unity's current Burst / Collections documentation:

- Burst works on HPC# and does not support managed objects in compiled code.
- Jobs are generally more optimal than function pointers for Burst code.
- `ParallelWriter` paths cannot grow capacity.
- parallel writes are not deterministic unless you use `NativeStream`/`UnsafeStream` or sort afterwards.

Implications for Sangria:

- use jobs, not delegates, as the main extensibility mechanism in the hot path
- pre-size all writable containers before parallel jobs
- if a preprocessing stage writes variable counts in parallel, use `NativeStream` or deterministic sort after collection
- keep the sweep in a single Burst job and parallelize only the embarrassingly parallel parts

## Direct Emission into Sangria Mesh

### Do not emulate `OutputPolymesh(...)`

LibTess emits:

- deduplicated output vertices
- `Elements[]`
- optional connected polygon information

Sangria does not need that shape internally.

For Sangria v1, emit directly to:

- point positions
- vertex-to-point map
- triangle primitive data

### Fast output path

Use Sangria's internal dense-topology facilities:

1. allocate dense point/vertex/primitive ranges once
2. prepare triangle storage once
3. fill arrays directly
4. mark topology/attribute changes once

Practical mapping:

- each unique tessellated output vertex -> one Sangria point
- each triangle corner -> one Sangria vertex referencing that point
- each triangle -> one primitive with 3 vertices

This makes triangulation output compatible with the existing:

- dense triangle primitive layout
- compile fast path
- Unity mesh export path

### Deduplication

The sweep workspace will often reuse vertices naturally through topology.

Recommendation:

- keep one explicit output remap from sweep vertex index -> Sangria point index
- do not rebuild uniqueness by hashing positions after triangulation

If the sweep creates intersection vertices, they should become first-class output points.

## Attribute Strategy

### Milestone 1

Only guarantee:

- point position output
- triangle topology output

This is enough to prove the core port.

### Milestone 2

Port the LibTess intersection weighting idea:

- `VertexWeights(...)`
- `GetIntersectData(...)`

and use it to interpolate Sangria point attributes when a new vertex is created by edge intersection or vertex merge.

Recommended approach:

- generated point stores provenance weights
- a second pass applies interpolation to supported point attributes
- unsupported attributes either:
  - use nearest/source copy policy, or
  - are explicitly rejected by options

Do not attempt generic attribute interpolation inside the sweep itself. Keep sweep topology-focused.

## Implementation Phases

### Phase 0: Benchmark and oracle setup

- build tests that compare output against the current LibTessDotNet integration
- create benchmark scenes / test data:
  - convex polygon
  - concave polygon
  - polygon with holes
  - duplicate vertices
  - collinear runs
  - self-touching / near-degenerate input
  - contours with intersections

Acceptance:

- baseline timings and allocations captured before the port starts

### Phase 1: Native input + projection utilities

- implement `NativeContourSet`
- implement normal calculation
- implement projection to sweep coordinates
- implement contour orientation normalization

Acceptance:

- projected coordinates match LibTess behavior on test cases

### Phase 2: Geometric predicate port

- port and validate `Geom` helpers
- add deterministic tests for:
  - ordering
  - orientation
  - intersection placement
  - winding-rule classification

Acceptance:

- predicate outputs match LibTess on a golden test set

### Phase 3: Indexed topology workspace

- implement vertices, half-edges, faces, free lists
- port the topology edit operations from `Mesh.cs`
- add invariant checks for debug/editor builds

Acceptance:

- topology operations pass local consistency tests before sweep logic is added

### Phase 4: Heap + active-region set

- implement removable min-heap
- implement active-region ordered structure
- add tests for insert/remove/update/find behavior under sweep ordering

Acceptance:

- data-structure behavior matches the current managed implementation on synthetic cases

### Phase 5: Sweep monotone partition

- port:
  - degenerate cleanup
  - event loop
  - left/right vertex connection logic
  - intersection handling
  - winding classification
- keep logic close to original control flow

Acceptance:

- interior regions match LibTess on golden cases
- no managed allocations in profiler during repeated runs

### Phase 6: Monotone triangulation

- port `TessellateMonoRegion(...)`
- gather triangles directly into dense output staging

Acceptance:

- triangle sets match LibTess topology on golden cases

### Phase 7: Dense Sangria emission

- bulk-emit points/vertices/primitives into `NativeDetail`
- use triangle fast path
- add compile/export tests

Acceptance:

- output can be compiled and exported through the existing Sangria mesh pipeline

### Phase 8: Attribute interpolation and optional features

- interpolate point attributes for generated points
- optionally add:
  - boundary-only output
  - connected polygon output
  - contour output adapters

Acceptance:

- attribute fidelity is verified on controlled test meshes

### Phase 9: Optimization pass

- profile large contour sets
- decide whether active-region storage needs a tree/skip-list upgrade
- decide whether `double` predicates should remain default
- tune capacity heuristics and no-resize mode

Acceptance:

- performance target is measured, not assumed

## Performance Rules for the Port

- No `class` allocations in the hot path.
- No delegates in the hot path.
- No LINQ.
- No boxing.
- No `IList<T>` in internal code.
- No exceptions for expected runtime flow.
- No per-element `NativeList` growth inside parallel jobs.
- No mutation of `NativeDetail` one point/primitive at a time during sweep.
- No post-triangulation dedupe pass based on hashing positions.
- Do not copy disposable native workspace structs by value.

Specific C# safety note:

- if a native workspace container must be mutated, do not declare it with `using var`
- allocate, mutate, then dispose deterministically in `finally` or in the owner `Dispose()`

## Debugging and Verification Plan

Use debug-only tooling during the port:

- a projected 2D sweep dump
- active-region ordering snapshots
- face/edge invariant validation
- optional adapter that emits intermediate sweep results into Sangria debug geometry

`DetailVisualizer.cs` is a useful pattern reference for visualization, but the triangulation runtime itself should not depend on editor-only drawing.

Recommended tests:

- exact triangle count comparison vs LibTess
- winding-rule comparison
- generated intersection vertex count comparison
- no-GC-allocation test for repeated warm calls
- Burst-enabled job execution tests
- randomized fuzz cases with stable seed

## What Not to Do

- Do not replace LibTess with ear clipping if you need holes, winding rules, and robust degeneracy handling.
- Do not expose LibTessDotNet types from the new Sangria API.
- Do not parallelize the sweep before the single-job version is correct and benchmarked.
- Do not start with custom `NativeContainer` work unless a public job-safe workspace API truly requires it.
- Do not port triangle strip / fan grouping before the triangle core is finished and measured.

## Licensing / Attribution

This work is derived from the behavior and structure of LibTess / libtess2 / LibTessDotNet.

Keep:

- the SGI Free Software License B notice where required
- attribution to the upstream lineage
- a short note in the new Sangria triangulation sources describing which upstream files informed the port

## Internet Findings That Affect the Design

These external references support the design choices above:

- Unity Burst manual: Burst only compiles the HPC# subset, does not support managed objects, and is best paired with jobs.
- Unity Burst function pointer guidance: jobs are more optimal than function pointers, and function pointers lose aliasing/vectorization opportunities.
- Unity Collections overview: `Unsafe*` collections are justified for nested/custom internal layouts, but `Native*` and `Unsafe*` are not meaningfully different for performance once safety checks are disabled.
- Unity Collections parallel writer guidance: parallel writers cannot grow capacity and write order is non-deterministic unless you use streams or sort later.
- libtess2 README: performance improved substantially by replacing many small allocations with a bucketed allocator and allowing operation on preallocated memory.

The immediate Sangria consequence is clear:

- build a reusable scratch arena first
- keep the sweep as a single Burst job
- parallelize preprocessing / output packing
- avoid public managed callback APIs in the hot path

## Source References

### Local source basis

- `ThirdParty/LibTessDotNet/Tess.cs`
- `ThirdParty/LibTessDotNet/Sweep.cs`
- `ThirdParty/LibTessDotNet/Mesh.cs`
- `ThirdParty/LibTessDotNet/MeshUtils.cs`
- `ThirdParty/LibTessDotNet/Geom.cs`
- `ThirdParty/LibTessDotNet/PriorityQueue.cs`
- `ThirdParty/LibTessDotNet/PriorityHeap.cs`
- `ThirdParty/LibTessDotNet/Dict.cs`
- `Scripts/Core/NativeDetail/NativeDetail.Primitive.cs`
- `Scripts/Core/NativeDetail/PrimitiveStorage.cs`
- `Scripts/Core/NativeDetail/NativeDetail.Compile.cs`
- `Scripts/Core/NativeDetail/NativeDetail.BurstJobs.cs`

### External references

- Unity Burst manual: https://docs.unity3d.com/kr/6000.0/Manual/script-compilation-burst.html
- Unity Collections overview: https://docs.unity.cn/Packages/com.unity.collections%402.5/manual/collections-overview.html
- Unity Collections parallel readers/writers: https://docs.unity.cn/Packages/com.unity.collections%402.2/manual/parallel-readers.html
- Unity Burst function pointers: https://docs.unity.cn/Packages/com.unity.burst%401.8/manual/csharp-function-pointers.html
- Unity NativeContainer copy semantics: https://docs.unity3d.com/ja/2022.3/Manual/job-system-copy-nativecontainer.html
- Unity custom `NativeContainer` guidance: https://docs.unity3d.com/cn/2023.2/Manual/job-system-custom-nativecontainer.html
- libtess2 algorithm outline: https://github.com/memononen/libtess2/blob/master/alg_outline.md
- libtess2 README: https://raw.githubusercontent.com/memononen/libtess2/master/README.md

## Recommended First Deliverable

The first real implementation milestone should be:

- contour input adapter
- projection
- `Geom` predicate port
- indexed topology workspace
- single-job sweep partition
- monotone triangulation
- dense triangle emission into an empty `NativeDetail`
- position-only output

That is the smallest version that proves the architecture and gives Sangria a fast, zero-GC triangulation core without overcommitting to editor-facing or attribute-heavy features too early.
