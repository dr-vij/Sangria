# SangriaMesh Provenance & Attribute Transfer — Work Report

**Date**: March 2026
**Scope**: Enterprise-grade provenance tracking and attribute interpolation for the SangriaMesh tessellation engine

---

## Executive Summary

Implemented a complete provenance tracking and attribute transfer system for SangriaMesh's native tessellation engine. The system tracks the origin of every output vertex through the sweep-line algorithm, enabling correct interpolation of arbitrary user attributes (UV, color, normals, custom data) on the tessellated output. The architecture follows enterprise DCC patterns (Houdini, Blender, OpenSubdiv) with clean separation between topology generation, provenance tracking, and attribute interpolation.

**Key deliverables**:
- Provenance tracking at all 14 vertex creation/merge/split points in the sweep-line algorithm
- Correct handling of cascading intersections via weighted provenance flattening
- Public API with backward-compatible overloads
- Type-erased attribute transfer system with per-attribute interpolation policies
- 13 unit tests covering identity, intersection, merge, cascading, coalescing, truncation, and reconstruction
- Zero overhead when provenance is not requested

---

## Problem Statement

### Background

SangriaMesh's tessellation engine is a ground-up native port of LibTessDotNet's sweep-line algorithm, redesigned for Unity's Burst/Collections ecosystem. The core tessellation was already complete and producing correct triangle topology.

### The Gap

When the tessellation engine creates new vertices (via edge-edge intersections, vertex merges, or edge splits), the original LibTessDotNet relied on a managed `CombineCallback` that let users blend custom data inline. This approach is:
1. **Incompatible with Burst** (managed delegate, GC allocations)
2. **Not composable** (blend logic baked into the callback, not reusable)
3. **Not industry-standard** (professional DCC tools separate topology from attribute transfer)

Without provenance data, users had no way to interpolate their own attributes (UV coordinates, vertex colors, normals, custom per-point data) onto the tessellated output.

### Requirements

1. Track the origin of every output vertex: which input vertices it came from, and with what weights
2. Handle cascading intersections correctly (vertex created by intersection A participates in intersection B)
3. Zero overhead when provenance is not requested
4. Backward-compatible API: existing code must work unchanged
5. Decoupled attribute transfer: user controls interpolation policy per-attribute
6. Burst-compatible: no managed allocations, `StructLayout.Sequential` data

---

## Analysis Phase

### Sweep-Line Algorithm Audit

Before implementation, a complete audit of the sweep-line algorithm identified **14 distinct code locations** where vertices are created, merged, or split. This was critical because the original plan only accounted for 3-4 locations.

| # | Location | File | What Happens |
|---|----------|------|--------------|
| 1 | `AddContour` | NativeTess.cs | Input vertices enter the mesh |
| 2 | `GetIntersectData` | NativeTessSweep.cs | Two edges cross, new vertex created |
| 3 | `SpliceMergeVertices` | NativeTessSweep.cs | Two coincident vertices merged |
| 4-5 | `CheckForRightSplice` (SplitEdge ×2) | NativeTessSweep.cs | Edge split at existing vertex |
| 6 | `CheckForRightSplice` (Merge) | NativeTessSweep.cs | Via SpliceMergeVertices |
| 7-8 | `CheckForLeftSplice` (SplitEdge ×2) | NativeTessSweep.cs | Edge split at destination |
| 9-12 | `CheckForIntersect` (SplitEdge ×4) | NativeTessSweep.cs | Edge split at event vertex |
| 13 | `CheckForIntersect` (double split + intersect) | NativeTessSweep.cs | Via GetIntersectData |
| 14 | `ConnectLeftDegenerate` | NativeTessSweep.cs | Degenerate topology handling |

### Critical Bug Discovery: Cascading Intersection Provenance Loss

During analysis, a critical design flaw was identified in the initial implementation approach:

**Problem**: `GetIntersectData` computes intersection weights (w0, w1, w2, w3) for 4 source vertices (orgUp, dstUp, orgLo, dstLo). If any of these source vertices was itself created by a previous intersection (and thus has composite provenance with up to 4 sources), taking only `Src0` from each loses the composite provenance permanently.

**Example scenario**:
```
Step 1: Edge1 × Edge2 → Vertex P1 (provenance: A×0.3, B×0.2, C×0.3, D×0.2)
Step 2: Edge3 passes through P1, new intersection uses P1 as orgUp with weight w0
Step 3: Only P1.Src0 = A is preserved → sources B, C, D lost forever
```

**Solution**: `CombineWeighted` — flattens all 4 source ProvenanceRecords by scaling each record's weights by the intersection weight, collecting up to 16 (source, weight) pairs, then coalescing duplicates, sorting, truncating to 4, and normalizing.

### Sorting Inconsistency

The initial `Combine` method sorted by weight descending for truncation but did not re-sort by sourceId ascending afterward. `CoalesceAndNormalize` sorted by sourceId. This meant identical logical provenance could have different field ordering depending on how it was created, violating the determinism invariant.

---

## Implementation Details

### Phase 0: Data Structures

**New file: `ProvenanceTypes.cs`** (281 lines)

Core data structures:

```csharp
public enum ProvenanceKind : byte { Identity, Intersection, Merge, EdgeSplit, Degenerate }

[StructLayout(LayoutKind.Sequential)]
public struct ProvenanceRecord  // 36 bytes
{
    public int Src0, Src1, Src2, Src3;    // Source point indices
    public float W0, W1, W2, W3;          // Normalized weights
    public byte Count;                    // Active sources (1..4)
    public ProvenanceKind Kind;           // Creation method
}

public struct ProvenanceMap : IDisposable
{
    public NativeArray<ProvenanceRecord> Records;
    public int SourcePointCount, OutputPointCount;
}
```

**Key methods on ProvenanceRecord**:

| Method | Purpose | Complexity |
|--------|---------|------------|
| `Identity(sourcePointId)` | Factory for original input vertices | O(1) |
| `Intersection(src0..3, w0..3)` | Factory for edge-edge intersections | O(1) + normalize |
| `Combine(a, b, kind)` | Merge two records (up to 8 sources) | O(k²) coalesce, k≤8 |
| `CombineWeighted(a,wa, b,wb, c,wc, d,wd, kind)` | Weighted flatten of 4 records (up to 16 sources) | O(k²) coalesce, k≤16 |
| `CoalesceAndNormalize()` | In-place cleanup of self | O(k²), k≤4 |

**Internal utilities**:
- `CoalesceSortTruncate(ids, ws, ref total, maxCount)` — shared logic extracted from `Combine`, `CombineWeighted`, and `CoalesceAndNormalize` to eliminate code duplication
- `AddSources(record, ids, ws, ref total)` — unpack record sources into arrays
- `AddSourcesScaled(record, scale, ids, ws, ref total)` — unpack with weight scaling
- `Normalize()` — divide all weights by their sum; fallback to dominant source on NaN/Inf/zero

### Phase 1: Sweep Core Instrumentation

**Modified file: `NativeTessMesh.cs`**

- Added `public bool trackProvenance` field
- Added `public UnsafeList<ProvenanceRecord> vertexProvenance` — grows synchronously with vertex allocation
- `AllocVertex()`: conditionally adds default provenance slot
- `Create()`: accepts `trackProvenance` parameter, conditionally allocates buffer
- `Dispose()` / `Reset()`: conditionally disposes/clears provenance buffer

**Modified file: `NativeTessSweep.cs`**

Instrumented all 14 provenance write points:

1. **`AddContour`** — `ProvenanceRecord.Identity(pointIndex)` for each input vertex
2. **`GetIntersectData`** — `ProvenanceRecord.CombineWeighted(provOrgUp, w0, provDstUp, w1, provOrgLo, w2, provDstLo, w3, Intersection)` — this is the critical fix for cascading intersections
3. **`SpliceMergeVertices`** — reads both provenance records **before** `MeshSplice` (which destroys one vertex), combines them, writes to surviving vertex. Includes fast path: if both are Identity with same sourceId, skip the full combine
4. **`CheckForRightSplice`** (×2 SplitEdge) — `SetEdgeSplitProvenance` copies from endpoint
5. **`CheckForRightSplice`** (×1 Merge) — via SpliceMergeVertices
6. **`CheckForLeftSplice`** (×2 SplitEdge) — `SetEdgeSplitProvenance` copies from destination
7. **`CheckForIntersect`** (×4 SplitEdge) — `SetEdgeSplitProvenance` copies from event vertex
8. **`CheckForIntersect`** (×1 Intersection) — via GetIntersectData
9. **`ConnectLeftDegenerate`** — `SetEdgeSplitProvenance` with `Kind=Degenerate`

**Helper method**: `SetEdgeSplitProvenance(ref mesh, vertexId, in source)` — optimized to write source directly and modify Kind in-place via `ElementAt` (avoids intermediate struct copy).

**All writes guarded**: `if (s.mesh.trackProvenance) { ... }` — zero overhead when provenance not requested.

### Phase 2: Output Emission

**Modified file: `NativeTess.cs`**

- `TessellateInternal` accepts `bool emitProvenance` parameter
- `EmitToNativeDetail` remaps provenance from internal mesh vertex IDs to dense output point indices using the same `n` mapping used for positions
- Pre-filters empty polygons in a single pass (eliminated double `FaceArea` computation from the original port)
- Creates `ProvenanceMap` with correct `SourcePointCount` and `OutputPointCount`

**Modified file: `NativeTessState.cs`**

- `Create` propagates `trackProvenance` to `TessMesh.Create`

### Phase 3: Public API

**Modified file: `Triangulation.cs`**

New overload (backward-compatible, existing overload unchanged):

```csharp
public static CoreResult TriangulateContours(
    in NativeContourSet contours,
    ref NativeDetail output,
    out ProvenanceMap provenance,
    in TriangulationOptions options = default);
```

Both overloads share `NativeTessAPI.TessellateInternal`, differing only in the `emitProvenance` flag.

### Phase 4: Attribute Transfer

**New file: `AttributeTransfer.cs`** (206 lines)

Three components:

1. **`InterpolationMode`** enum: `Linear`, `Nearest`, `None`
2. **`InterpolationPolicy`** struct: per-attribute mode overrides via `NativeParallelHashMap<int, InterpolationMode>`, with default mode
3. **`AttributeTransferOp`** static class:
   - `TransferPointAttributes` — type-erased bulk transfer across all point attribute columns
   - `TransferAttribute<T>` — typed single-attribute transfer
   - `BlendColumn` — internal column-level blend loop
   - `BlendSingle` — per-point blend logic with stride-based float operations

**Blend logic**:
- `Count == 0` → `MemClear` (zero-fill)
- `Count == 1` or `Nearest` → `MemCpy` from `Src0`
- Linear + float stride → weighted sum: `dst[j] = Σ(Wi * src[SrcI][j])` for each float component
- Non-float stride (% 4 != 0) → automatic fallback to Nearest

**Modified files for transfer support**:
- `AttributeStore.cs` — added `RegisterAttributeRaw(id, stride, typeHash)` for type-erased attribute creation
- `NativeDetail.AttributesResources.cs` — added column access helpers: `GetPointAttributeColumnCount`, `GetPointAttributeColumnAt`, `HasPointAttribute`, `AddPointAttributeRaw`, `GetPointAttributeColumnByIdUnchecked`

### Phase 5: Optimizations Applied

| Optimization | Location | Impact |
|-------------|----------|--------|
| Conditional provenance allocation | `TessMesh.Create` | Zero memory overhead when provenance not requested |
| `trackProvenance` guards on all 14 write points | `NativeTessSweep.cs` | Zero compute overhead when provenance not requested |
| Fast path in `SpliceMergeVertices` | `NativeTessSweep.cs` | Skips full `Combine` for identical Identity records (common case for duplicate input vertices) |
| In-place Kind modification via `ElementAt` | `SetEdgeSplitProvenance` | Avoids 36-byte intermediate struct copy |
| Extracted `CoalesceSortTruncate` | `ProvenanceTypes.cs` | Eliminates code duplication between `Combine`, `CombineWeighted`, `CoalesceAndNormalize` |
| Pre-filter empty polygons | `EmitToNativeDetail` | Eliminates double `FaceArea` computation (was: compute in counting pass + compute again in emit pass) |
| Deterministic sourceId sort after truncation | `CoalesceSortTruncate` | Fixes inconsistency between `Combine` and `CoalesceAndNormalize` sort order |

---

## Testing

### Unit Tests (13 total in `SangriaMeshProvenanceTests.cs`)

#### Identity & Basic Provenance

| Test | What It Verifies |
|------|------------------|
| `Provenance_SimpleTriangle_AllIdentity` | 3-point triangle: all outputs are Identity, Count=1, W0=1.0, all 3 source IDs covered |
| `Provenance_ConcavePolygon_AllIdentity` | 5-point concave polygon: no intersections, all Identity |
| `Provenance_HolePolygon_WeightSumInvariant` | Square with hole: weight sum = 1.0 for all points, source IDs in range |
| `Provenance_WithoutProvenance_BackwardCompatible` | Old API (no provenance): produces correct output, no crash |
| `Provenance_IdentityPassthrough_ReconstructsPositionFromProvenance` | Triangle: position reconstructed from provenance matches actual output (1e-4 tolerance) |

#### Intersection & Overlap

| Test | What It Verifies |
|------|------------------|
| `Provenance_IntersectingContours_GeneratedPointsHaveMultipleSources` | Two overlapping squares (NonZero winding): all points have valid Count, weight sum = 1.0 |
| `Provenance_IntersectingContours_PositionReconstructionFromProvenance` | Two overlapping squares: position reconstructed from provenance matches actual output (0.05 tolerance — max 4 sources, no cascading) |

#### Cascading Intersections

| Test | What It Verifies |
|------|------------------|
| `Provenance_CascadingIntersections_InvariantsAndIdentityReconstruction` | Three overlapping squares (triggers cascading): all invariants (weight sum, source validity, ascending sort, positive weights), Identity points reconstruct exactly (1e-5), both identity and generated points exist |

#### ProvenanceRecord Unit Tests

| Test | What It Verifies |
|------|------------------|
| `ProvenanceRecord_Combine_CoalescesDuplicates` | Two Identity(5) combined → single source, Count=1, W0=1.0 |
| `ProvenanceRecord_Combine_TruncatesToFour` | Two 4-source records combined → truncated to 4, weights normalized |
| `ProvenanceRecord_Combine_SortsBySourceIdAscending` | Sources {7,3} + {1,5} → sorted ascending in output |
| `ProvenanceRecord_Intersection_NormalizesWeights` | Intersection(0.3, 0.2, 0.3, 0.2) → normalized to sum 1.0 |
| `ProvenanceRecord_CombineWeighted_FlattensCompositeRecords` | Composite (2-source) + 3 Identity → 5 sources truncated to 4, exact pre-calculated weights verified |
| `ProvenanceRecord_CombineWeighted_CoalescesDuplicateSources` | Two records sharing Src0 → coalesced weight, exact pre-calculated values |
| `ProvenanceRecord_CombineWeighted_NoTruncation_ExactValues` | 4 Identity records → no truncation, exact weights (0.4, 0.3, 0.2, 0.1), position reconstruction verified |

### Test Design Principles

1. **No tolerance fudging**: Tests with exact expected values where mathematically possible. Only use tolerance where floating-point arithmetic inherently requires it.
2. **Pre-calculated values**: CombineWeighted tests compute expected weights analytically (e.g., `0.30f / 0.90f`) rather than relying on the code under test.
3. **Invariant-based testing for complex cases**: Cascading intersections test verifies structural invariants (weight sum, source validity, ordering) for all points, and exact reconstruction only for Identity points where truncation is impossible.
4. **Separate unit vs integration**: ProvenanceRecord methods tested in isolation (no tessellation involved), then integration tests verify end-to-end behavior.

---

## Files Summary

### New Files

| File | Lines | Description |
|------|-------|-------------|
| `ProvenanceTypes.cs` | 281 | ProvenanceRecord, ProvenanceKind, ProvenanceMap |
| `AttributeTransfer.cs` | 206 | InterpolationMode, InterpolationPolicy, AttributeTransferOp |
| `SangriaMeshProvenanceTests.cs` | 780 | 13 unit tests |
| `DOCUMENTATION.md` | 695 | Complete library documentation |
| `WORK_REPORT.md` | this file | Detailed work report |
| `interpolation plan.md` | updated | Actualized plan with completed/future items |

### Modified Files

| File | Key Changes |
|------|-------------|
| `NativeTessMesh.cs` | `trackProvenance` flag, `vertexProvenance` UnsafeList, conditional alloc/grow/dispose |
| `NativeTessSweep.cs` | 14 provenance write points, `SetEdgeSplitProvenance` helper, `trackProvenance` guards, fast path in `SpliceMergeVertices` |
| `NativeTess.cs` | `emitProvenance` parameter, provenance remapping in `EmitToNativeDetail`, empty polygon pre-filter optimization |
| `NativeTessState.cs` | `trackProvenance` propagation to `TessMesh.Create` |
| `Triangulation.cs` | New overload with `out ProvenanceMap` |
| `AttributeStore.cs` | `RegisterAttributeRaw` for type-erased registration |
| `NativeDetail.AttributesResources.cs` | Column access helpers for transfer |

---

## Verification

- All source files pass static analysis (lint) with zero errors
- Tests pass in Unity test runner (confirmed by user)
- Backward compatibility verified: existing API overload produces identical output
- No `.csproj` or `.meta` files manually modified (per project guidelines)

---

## Lessons Learned

1. **Audit all code paths first**: The initial plan accounted for ~4 provenance write points. The actual sweep algorithm has 14. Skipping the audit would have resulted in missing provenance for EdgeSplit and Degenerate vertices.

2. **Cascading intersections are non-obvious**: The bug where `GetIntersectData` only used `Src0` from each source vertex was subtle. It only manifests when intersection vertices participate in subsequent intersections — which requires specific geometric configurations (3+ overlapping contours).

3. **Don't fudge test tolerances**: When a test fails with 0.33 error, the correct response is to design better tests that validate at the right abstraction level — not to increase tolerance to 0.5. Invariant-based testing (weight sum, source validity, ordering) plus exact testing of simple cases is more robust than approximate testing of complex cases.

4. **Enterprise patterns pay off**: Separating provenance from attribute transfer (Houdini model) made each component independently testable and the API composable. The `InterpolationPolicy` pattern allows per-attribute behavior without modifying the core.
