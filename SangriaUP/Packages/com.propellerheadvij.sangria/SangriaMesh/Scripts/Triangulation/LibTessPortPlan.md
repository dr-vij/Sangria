# LibTessDotNet Port Plan for Sangria Mesh — Remaining Work

## Immediate Plan (Now)

1. Keep triangulation core zero-allocation and topology-focused.
2. Emit blend/provenance data for generated vertices (`source ids + weights`), without automatic attribute interpolation in core.
3. Expose this data to callers so interpolation policy stays user-defined.

## Completed

The core port is done. The following are already implemented and integrated:

- Public API: `NativeContourSet`, `TriangulationOptions`, `Triangulation` static class
- Geometric predicates: `NativeTessGeom.cs`
- Indexed topology workspace: `NativeTessMesh.cs`, `NativeTessTypes.cs`
- Priority queue: `NativeTessPQ.cs`
- Active-region ordered list: `NativeTessDict.cs`
- Sweep state: `NativeTessState.cs`
- Sweep-line monotone partition + monotone triangulation: `NativeTessSweep.cs`
- Contour input, projection, dense NativeDetail emission: `NativeTess.cs`
- Routing through `NativeTessAPI.Tessellate()` in `Triangulation.cs`

No LibTessDotNet files were modified. No bridges between libraries.

---

## Remaining Work

### Phase 0: Benchmark and Oracle Setup

- Build tests that compare output against the current LibTessDotNet integration
- Create benchmark test data:
  - convex polygon
  - concave polygon
  - polygon with holes
  - duplicate vertices
  - collinear runs
  - self-touching / near-degenerate input
  - contours with intersections
- Capture baseline timings and allocations

### Phase 8: Attribute Interpolation and Optional Features

- Port LibTess intersection weighting (`VertexWeights`, `GetIntersectData`)
- Use it to interpolate Sangria point attributes when a new vertex is created by edge intersection or vertex merge
- Generated point stores provenance weights; a second pass applies interpolation to supported point attributes
- Optionally add:
  - boundary-only output
  - connected polygon output
  - contour output adapters

### Phase 9: Optimization Pass

- Profile large contour sets
- Decide whether active-region storage needs a tree/skip-list upgrade
- Decide whether `double` predicates should remain default
- Tune capacity heuristics and no-resize mode (`StrictNoResize` / `CoreResult.CapacityExceeded`)

### Job System Integration

- Wrap the core sweep as a single Burst `IJob`
- Parallelize where profitable:
  - contour adaptation and prefix sums
  - projection of 3D points to 2D sweep coordinates
  - optional contour orientation checks
  - output packing into dense point/vertex/triangle buffers
  - attribute interpolation for generated points
- Pre-size all writable containers before parallel jobs
- Use `NativeStream` or deterministic sort for variable-count parallel writes

### Testing and Verification

- Exact triangle count comparison vs LibTess
- Winding-rule comparison across all rules
- Generated intersection vertex count comparison
- No-GC-allocation test for repeated warm calls
- Burst-enabled job execution tests
- Randomized fuzz cases with stable seed

### Debug Tooling (Optional)

- Projected 2D sweep dump
- Active-region ordering snapshots
- Face/edge invariant validation
- Optional adapter emitting intermediate sweep results into Sangria debug geometry

---

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
- libtess2 algorithm outline: https://github.com/memononen/libtess2/blob/master/alg_outline.md
- libtess2 README: https://raw.githubusercontent.com/memononen/libtess2/master/README.md
