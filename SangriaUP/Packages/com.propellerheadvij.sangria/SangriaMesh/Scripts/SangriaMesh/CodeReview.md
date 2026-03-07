# CodeReview: SangriaMesh package status and performance roadmap

Date: 2026-03-06
Scope: `Packages/com.propellerheadvij.sangria/SangriaMesh`
Constraints: build/tests were not run by request.

## 1. Current Development Status

The package currently contains two independent stacks:

1. `SangriaMesh` (new node-oriented core)
- strong data model (`NativeDetail` -> `NativeCompiledDetail`)
- stable handle lifecycle (`SparseHandleSet` + generations)
- domain attributes/resources with typed accessors
- dense triangle conversion fast path to Unity MeshData API

2. `ViJMeshTools` (legacy cutter/tessellation)
- plane cut + contour reconstruction + LibTess triangulation
- separate data model, not integrated into the new core snapshot pipeline
- contains most of the highest-cost runtime code paths for cutting

Net: core architecture is solid and modernized, but high-load cutting/tessellation is still in the legacy path.

## 2. Findings (ordered by severity)

### Critical

1. Possible invalid memory usage in watertight validator loop
- File: `Scripts/MeshOpps/MeshWatertightValidationJob.cs`
- Lines: 35, 38-58
- `vertices` is allocated once before submesh loop and disposed inside the loop (`line 57`).
- On meshes with more than one submesh, later iterations may access a disposed container.
- Impact: correctness risk (undefined behavior/crash), not only performance.

### High

1. O(n^2) contour consolidation in hot cutting path
- File: `Scripts/Helpers/TessAdapter.cs`
- Lines: 47-49, 168-208
- `CreateContours()` uses `ConsolidateContoursFromSegmentsBruteforce(...)` directly.
- This is quadratic and managed-allocation-heavy for large cut segment counts.
- Impact: major CPU growth as cut complexity increases.

2. Triangle splitting duplicates geometry aggressively
- File: `Scripts/MeshOpps/CutMeshWithPlaneJob.cs`
- Lines: 130-387
- Intersected triangles are expanded by duplicating vertices per output triangle.
- No intersection vertex reuse cache; no parallel partitioning by triangle blocks.
- Impact: large memory traffic + extra write amplification in dense meshes.

3. Full snapshot rebuild on every `Compile()` call
- File: `PropellerheadMesh/Scripts/SangriaMesh/NativeDetail.cs`
- Lines: 547-737, 788-883
- Even small edits rebuild dense topology and all packed attribute/resource buffers.
- Impact: can dominate frame time in node graphs with frequent micro-updates.

4. General polygon conversion path is managed and non-linear
- File: `PropellerheadMesh/Scripts/Generators/SangriaMeshUnityMeshExtensions.cs`
- Lines: 243-390, 532-611
- Uses managed arrays/lists and ear clipping fallback on polygons.
- Impact: much slower than triangle fast path and more GC pressure.

### Medium

1. Legacy support contract mismatch risk
- File: `Scripts/MeshOpps/MeshSupportedValidator.cs`
- Lines: 12-40
- Comment says cutter supports `ushort` index format; runtime checks only vertex attributes.
- Impact: hidden runtime assumptions and debugging cost.

## 3. Dramatic-Only Performance Improvements

Only improvements with potential for **step-change** speedups are listed below.

1. Unify cutting pipeline around dense `SangriaMesh` core + parallel clipping
- Move plane-cut output directly into `NativeDetail`/compiled buffers.
- Replace current serial triangle split with Burst-parallel triangle processing (`IJobParallelFor`) and edge-intersection cache (quantized key -> intersection vertex index).
- Why dramatic: removes duplicated conversion layers and reduces repeated writes/allocations across the whole pipeline.

2. Replace contour reconstruction+tessellation kernel
- Current bottleneck: bruteforce contour stitch + LibTess managed preprocessing.
- High-impact options:
  - robust path: native constrained triangulation/clip backend (e.g., CGAL-style clipping/triangulation pipeline)
  - speed path: earcut-style z-order triangulation in native/Burst-friendly implementation
- Why dramatic: this stage is currently algorithmically super-linear and allocation-heavy.

3. Add incremental compile mode (dirty ranges) for `NativeDetail`
- Keep compiled buffers alive and patch only dirty domains/elements instead of full rebuild.
- Track dirty point/vertex/primitive ranges + dirty attributes/resources.
- Why dramatic: in node workflows with local edits, work drops from O(total mesh) to O(changed subset).

4. Add GPU slicing/triangulation path for very large meshes
- Keep CPU/Burst path for small-medium meshes.
- Route high-poly batches to compute path.
- Why dramatic: modern GPU slicing/triangulation research reports order-of-magnitude acceleration when data size is large enough.

## 4. Internet Research That Informed Recommendations

1. Unity MeshData API and overhead guidance
- Unity docs recommend using `MeshDataArray` and batching operations to reduce safety/validation overhead.
- Source: https://docs.unity3d.com/cn/current/ScriptReference/Mesh.MeshData.html

2. Unity Burst optimization guidance
- Burst docs emphasize SIMD-friendly patterns (e.g., vectorized math) and direct code generation inspection for hot loops.
- Source: https://docs.unity.cn/Packages/com.unity.burst@1.7/manual/docs/OptimizationGuidelines.html

3. Earcut algorithm characteristics
- Earcut uses z-order curve hashing for acceleration but does not guarantee correctness for all pathological polygons.
- Source: https://github.com/mapbox/earcut.hpp

4. CGAL polygon mesh slicing/clipping capabilities
- CGAL provides robust mesh slicing and clipping primitives (AABB-tree-backed slicer, clipping APIs), useful as a robust backend direction.
- Source: https://doc.cgal.org/latest/Polygon_mesh_processing/index.html

5. Robust triangulation foundations
- Shewchuk's work (Triangle) documents robust exact-predicate triangulation and engineering tradeoffs for reliability/performance.
- Source: https://www.cs.cmu.edu/~quake/tripaper/triangle0.html

6. GPU triangulation/slicing scale results
- Recent GPU triangulation/meshing papers report large speedups for massive geometric workloads.
- Sources:
  - https://arxiv.org/abs/2405.10961
  - https://arxiv.org/abs/2405.04067

## 5. Conclusion

Current package direction is correct: the new `SangriaMesh` core is structurally strong and much closer to high-performance node workflows than the legacy cutter stack. To reach maximum speed, the major gains are not in micro-optimizations; they are in architectural migration of cut/tessellation to dense parallel data paths, incremental compile, and optional GPU offload for the large-mesh tier.

