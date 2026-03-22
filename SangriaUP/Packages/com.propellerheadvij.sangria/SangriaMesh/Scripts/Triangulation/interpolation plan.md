# Interpolation Plan — Enterprise Attribute Transfer for SangriaMesh

## Current Status

Phases 0–4 are **complete and tested**. The system provides:
- Provenance tracking at all 14 sweep vertex creation sites
- Cascading intersection support via `CombineWeighted` flattening
- Public API with backward-compatible overloads
- Type-erased attribute transfer with per-attribute interpolation policies
- 13 unit tests covering all key scenarios
- Zero overhead when provenance is not requested

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Layer 4: User Code                                     │
│  - Chooses which attributes to transfer                 │
│  - Sets per-attribute InterpolationPolicy               │
│  - Calls AttributeTransferOp                            │
├─────────────────────────────────────────────────────────┤
│  Layer 3: AttributeTransferOp (high-level API)          │
│  - TransferPointAttributes (type-erased, all columns)   │
│  - TransferAttribute<T> (typed, single attribute)       │
│  - Stride-based linear blend / nearest / none           │
├─────────────────────────────────────────────────────────┤
│  Layer 2: InterpolationPolicy                           │
│  - Per-attribute mode overrides                         │
│  - Default mode: Linear                                 │
│  - Modes: Linear, Nearest, None                         │
├─────────────────────────────────────────────────────────┤
│  Layer 1: ProvenanceMap (immutable after sweep)          │
│  - NativeArray<ProvenanceRecord> aligned to output pts  │
│  - Created by triangulation, consumed by transfer       │
├─────────────────────────────────────────────────────────┤
│  Layer 0: Triangulation Core (sweep)                    │
│  - Emits ProvenanceRecord at all vertex creation sites  │
│  - Zero-allocation provenance tracking in TessMesh      │
│  - Conditional: trackProvenance flag gates all writes   │
└─────────────────────────────────────────────────────────┘
```

---

## Improvement Proposals (Prioritized)

### Priority 1 — High Value, Low Effort

#### 1.1 Burst `IJobParallelFor` for Attribute Transfer
**Effort**: ~1 day | **Impact**: 3–10× speedup for large meshes with many attributes

Currently `BlendColumn` is a sequential `for` loop. Wrapping it in a Burst-compiled `IJobParallelFor` would parallelize the blend across output points.

```csharp
[BurstCompile]
struct BlendAttributeJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<ProvenanceRecord> Provenance;
    [ReadOnly] public NativeArray<byte> SourceBuffer;
    [NativeDisableParallelForRestriction]
    public NativeArray<byte> DestBuffer;
    public int Stride;
    public InterpolationMode Mode;
    
    public void Execute(int outputPointIndex) { /* blend logic */ }
}
```

**Reference**: Houdini's attribute evaluation is always multi-threaded. Blender's Geometry Nodes `Transfer Attribute` uses parallel evaluation.

#### 1.2 Slerp InterpolationMode for Normals and Quaternions
**Effort**: ~0.5 day | **Impact**: Correct normal/quaternion interpolation

Add `InterpolationMode.Slerp = 3` that uses spherical linear interpolation for `float4`/`quaternion` types. Fallback to linear for non-quaternion types.

**Reference**: Houdini's `attribinterpolate` has `slerp` mode for quaternion type info. OpenSubdiv uses special stencil evaluation for normals.

#### 1.3 Bounds Validation in ProvenanceMap
**Effort**: ~0.5 day | **Impact**: Earlier error detection

Add `ProvenanceMap.Validate()` that checks all invariants:
- Weight sum ∈ [1.0 ± ε] for every record
- All SrcN < SourcePointCount when N < Count
- Sources sorted ascending
- Count ∈ [1, 4]

Useful for debugging and as a safety net for complex tessellation cases.

### Priority 2 — Medium Value, Medium Effort

#### 2.1 Vertex-Domain Attribute Transfer
**Effort**: ~2 days | **Impact**: Per-corner attributes (UV seams, hard edges)

Vertex-domain attributes (e.g., UV coordinates at polygon corners) need different transfer logic because a single point can have multiple vertex values. Requires mapping from output vertices to source vertices via face adjacency.

**Reference**: Houdini's vertex attributes (UV, Cd on vertex domain). Blender's corner domain in Geometry Nodes.

#### 2.2 Primitive-Domain Attribute Transfer
**Effort**: ~1 day | **Impact**: Material IDs, face groups

Primitive-domain attributes (e.g., material ID per triangle) should use `Nearest` mode by default — the triangle inherits from the source face it belongs to. Requires additional provenance: which source contour/region each output face came from.

**Reference**: Houdini primitive attributes. Blender face domain.

#### 2.3 Custom Blend via FunctionPointer
**Effort**: ~1 day | **Impact**: User-defined interpolation

Add `InterpolationMode.Custom = 255` with `FunctionPointer<BlendDelegate>` registration per attribute. Enables advanced interpolation strategies (e.g., log-space blending for HDR colors, geodesic for tangent frames).

```csharp
[BurstCompile]
public unsafe delegate void BlendDelegate(byte* src, byte* dst, float weight, bool first, int stride);

policy.SetCustomMode(attributeId, FunctionPointer<BlendDelegate>.Compile(myBlendFunc));
```

**Reference**: Houdini's custom VEX attribute transfer. Unity Burst function pointers.

#### 2.4 ProvenanceRecord Compression for Identity-Heavy Meshes
**Effort**: ~1 day | **Impact**: Memory reduction for simple polygons

For meshes without intersections (convex/concave polygons, holes), all provenance records are Identity. A flag on ProvenanceMap indicating "all identity" could skip the per-record storage entirely and use a simple index remap.

### Priority 3 — High Value, High Effort (Future Architecture)

#### 3.1 Variable-Length Provenance (OpenSubdiv Stencil Model)
**Effort**: ~3–5 days | **Impact**: Eliminates truncation error entirely

Replace fixed 4-slot ProvenanceRecord with variable-length stencils stored in a packed buffer:
```
StencilTable:
  offsets[outputPointCount+1]   // prefix-sum into packed arrays
  sourceIds[totalSources]       // all source indices, packed
  weights[totalSources]         // all weights, packed
```

This eliminates the 4-source truncation that causes approximation error in cascading intersections. However, it requires dynamic allocation during sweep (or two-pass: count first, then write).

**Reference**: OpenSubdiv `Far::StencilTable`. This is the industry gold standard for unlimited provenance.

#### 3.2 Cross-Domain Promotion/Demotion
**Effort**: ~3 days | **Impact**: Houdini-level attribute flexibility

Support attribute transfer between domains:
- Point → Vertex (replicate point value to all vertices)
- Vertex → Point (average vertex values at shared point)
- Primitive → Point (distribute face value to corners)
- Point → Primitive (aggregate point values, e.g., average)

**Reference**: Houdini's `attribpromote` SOP. Blender's `Attribute Domain` conversion node.

#### 3.3 Lazy/Streaming Provenance for Very Large Meshes
**Effort**: ~2–3 days | **Impact**: Reduced peak memory for large contour sets

Instead of storing all provenance in memory, emit provenance records directly into the attribute transfer pipeline as vertices are finalized. This reduces peak memory from O(outputVertices × 36 bytes) to O(1) per attribute column.

Requires rethinking the emit pipeline to interleave provenance consumption with output emission.

### Priority 4 — Low Priority (Nice to Have)

#### 4.1 Debug Visualization of Provenance
**Effort**: ~1 day | **Impact**: Development/debugging aid

Gizmo/overlay that color-codes output vertices by ProvenanceKind (Identity=green, Intersection=red, Merge=yellow, etc.) and draws lines to source vertices with opacity proportional to weight.

#### 4.2 ProvenanceMap Serialization
**Effort**: ~0.5 day | **Impact**: Caching, offline processing

Binary serialize/deserialize ProvenanceMap for caching or offline attribute transfer workflows.

#### 4.3 Attribute Transfer Profiling Markers
**Effort**: ~0.5 day | **Impact**: Performance instrumentation

Add Unity Profiler markers around BlendColumn and TransferPointAttributes for performance analysis.

#### 4.4 SelectiveTransferPointAttributes with Attribute ID Filter
**Effort**: ~0.5 day | **Impact**: Fine-grained control

```csharp
AttributeTransferOp.TransferPointAttributes(
    ref source, ref destination, in provenance,
    NativeArray<int> attributeIds,   // only transfer these
    in policy);
```

---

## Provenance Write Points Reference (14 Total)

| Location | Kind | Description |
|----------|------|-------------|
| AddContour | Identity | Each input vertex gets `{Src=pointIndex, W=1.0}` |
| GetIntersectData | Intersection | 4 source vertices, weighted via `CombineWeighted` (handles cascading) |
| SpliceMergeVertices | Merge | Combine provenance of both vertices before splice (fast path for identical Identity) |
| CheckForRightSplice (SplitEdge ×2) | EdgeSplit | Copy from nearest endpoint |
| CheckForRightSplice (Merge ×1) | Merge | Via SpliceMergeVertices |
| CheckForLeftSplice (SplitEdge ×2) | EdgeSplit | Copy from destination endpoint |
| CheckForIntersect (SplitEdge ×4) | EdgeSplit | Copy from event vertex |
| CheckForIntersect (double split + intersect) | Intersection | Via GetIntersectData |
| ConnectLeftDegenerate (SplitEdge ×1) | Degenerate | Copy from event vertex |

---

## Invariants (Contract)

1. **Weight normalization**: `∑Wi = 1.0 ± ε` for every ProvenanceRecord
2. **Source validity**: all `SrcN < SourcePointCount` when `N < Count`
3. **Deterministic ordering**: sources sorted by sourceId ascending
4. **Kind accuracy**: ProvenanceKind reflects the actual creation method
5. **Backward compatibility**: old API without provenance works unchanged, zero overhead
6. **Zero managed allocations**: all hot path through UnsafeList/NativeArray

---

## Completed Phases (Reference)

### Phase 0: Data Structures ✅
- `ProvenanceRecord` (fixed 4-slot, 36 bytes, `StructLayout.Sequential`)
- `ProvenanceKind` enum (Identity, Intersection, Merge, EdgeSplit, Degenerate)
- `ProvenanceMap` struct with `NativeArray<ProvenanceRecord>`
- `CombineWeighted` for cascading intersection flattening
- `CoalesceSortTruncate` shared utility (eliminated code duplication)
- Fallback policy: NaN/Inf/zero sum → collapse to dominant source

### Phase 1: Core Provenance Emission ✅
- All 14 provenance write points instrumented
- `trackProvenance` conditional guards (zero overhead when not requested)
- `vertexProvenance` grows synchronously with `AllocVertex()`
- Fast path in `SpliceMergeVertices` for identical Identity records
- Optimized `SetEdgeSplitProvenance` via in-place `ElementAt` modification

### Phase 2: Output Emission ✅
- Provenance remapping from mesh vertex IDs to dense output point indices
- Pre-filter empty polygons optimization (eliminated double FaceArea)
- `ProvenanceMap` creation with `SourcePointCount` and `OutputPointCount`

### Phase 3: Public API ✅
- New `Triangulation.TriangulateContours` overload with `out ProvenanceMap`
- Old overload unchanged (backward compatible)
- Propagation of `emitProvenance` flag through `NativeTessState` → `TessMesh`

### Phase 4: Attribute Transfer ✅
- `InterpolationMode` enum: Linear, Nearest, None
- `InterpolationPolicy` with per-attribute overrides via `NativeParallelHashMap`
- `AttributeTransferOp.TransferPointAttributes` — type-erased bulk transfer
- `AttributeTransferOp.TransferAttribute<T>` — typed single-attribute transfer
- Stride-based float blend, automatic Nearest fallback for non-float types
- `AttributeStore.RegisterAttributeRaw` for type-erased creation
- Column access helpers in `NativeDetail.AttributesResources`

### Phase 5: Testing ✅
- 13 unit tests covering identity, intersection, merge, cascading, coalescing, truncation, reconstruction
- Pre-calculated expected values (no tolerance fudging)
- Invariant-based testing for complex geometry
- All tests passing

---

## Files Reference

### New Files
- `ProvenanceTypes.cs` — ProvenanceRecord, ProvenanceKind, ProvenanceMap
- `AttributeTransfer.cs` — InterpolationMode, InterpolationPolicy, AttributeTransferOp
- `SangriaMeshProvenanceTests.cs` — 13 unit tests
- `DOCUMENTATION.md` — Complete library documentation
- `WORK_REPORT.md` — Detailed work report

### Modified Files
- `NativeTessMesh.cs` — vertexProvenance buffer, trackProvenance flag
- `NativeTessSweep.cs` — 14 provenance write points, optimizations
- `NativeTess.cs` — emitProvenance parameter, EmitToNativeDetail remapping
- `NativeTessState.cs` — trackProvenance propagation
- `Triangulation.cs` — New public overload with provenance
- `AttributeStore.cs` — RegisterAttributeRaw
- `NativeDetail.AttributesResources.cs` — Column access helpers
