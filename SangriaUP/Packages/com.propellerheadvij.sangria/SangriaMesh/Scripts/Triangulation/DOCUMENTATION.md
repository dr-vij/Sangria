# SangriaMesh Triangulation — Complete Documentation

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Core Concepts](#core-concepts)
4. [API Reference](#api-reference)
5. [Provenance System](#provenance-system)
6. [Attribute Transfer System](#attribute-transfer-system)
7. [Usage Guide](#usage-guide)
8. [Comparison with Industry Solutions](#comparison-with-industry-solutions)
9. [Performance Characteristics](#performance-characteristics)
10. [Design Decisions and Rationale](#design-decisions-and-rationale)
11. [Limitations and Known Trade-offs](#limitations-and-known-trade-offs)
12. [Troubleshooting](#troubleshooting)

---

## Overview

SangriaMesh Triangulation is a Burst-compatible, zero-managed-allocation constrained polygon tessellation engine for Unity. It converts 2D contour inputs (including contours with holes, overlapping contours, self-intersections, and degenerate geometry) into indexed triangle meshes stored in `NativeDetail`.

The engine is a ground-up native port of LibTessDotNet's sweep-line algorithm, redesigned for:
- **Unity Collections / Burst compatibility** — all data in `UnsafeList`, `NativeArray`, no managed heap
- **Attribute-agnostic provenance tracking** — every output vertex records which input vertices it came from and with what weights
- **Decoupled attribute transfer** — interpolation of arbitrary user attributes (UV, color, normals, custom data) is a separate post-processing step, never embedded in the sweep core

This mirrors the architecture used in professional DCC tools (Houdini, Blender Geometry Nodes, Unreal PCG Framework) where geometry operations produce topology + provenance, and attribute transfer is a separate, composable operation.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Layer 4: User Code                                             │
│  - Prepares NativeContourSet with positions and contour offsets │
│  - Calls Triangulation.TriangulateContours                      │
│  - Optionally requests ProvenanceMap                            │
│  - Optionally calls AttributeTransferOp with custom policy      │
├─────────────────────────────────────────────────────────────────┤
│  Layer 3: AttributeTransferOp (high-level post-process)         │
│  - TransferPointAttributes: type-erased, all columns at once    │
│  - TransferAttribute<T>: typed, single attribute                │
│  - Stride-based linear blend / nearest / none modes             │
├─────────────────────────────────────────────────────────────────┤
│  Layer 2: InterpolationPolicy (per-attribute mode selection)     │
│  - Default mode: Linear for all attributes                      │
│  - Per-attribute overrides via NativeParallelHashMap             │
│  - Modes: Linear, Nearest, None                                 │
├─────────────────────────────────────────────────────────────────┤
│  Layer 1: ProvenanceMap (immutable provenance data)              │
│  - NativeArray<ProvenanceRecord> aligned to output point indices│
│  - Created by triangulation core, consumed by transfer layer    │
│  - Does not know about attributes or their types                │
├─────────────────────────────────────────────────────────────────┤
│  Layer 0: Triangulation Core (sweep-line engine)                │
│  - NativeTessSweep: monotone decomposition + triangulation      │
│  - NativeTessMesh: indexed half-edge mesh workspace             │
│  - Emits ProvenanceRecord at every vertex creation/merge site   │
│  - Zero managed allocations, Burst-compatible                   │
└─────────────────────────────────────────────────────────────────┘
```

### Key Architectural Principle: Separation of Topology and Attributes

The triangulation core (Layer 0) **never** reads or writes user attributes. It produces:
1. **Topology** — triangles as indexed primitives in `NativeDetail`
2. **Provenance** — a map from each output vertex to its source input vertices and interpolation weights

This separation is fundamental to the design and mirrors the Houdini SOP model where:
- A geometry operation (e.g., PolyReduce, Boolean, Remesh) emits new topology + provenance
- Attribute transfer is a separate, composable pass (`attribtransfer`, `attribute interpolate`)
- The user controls interpolation policy per-attribute (linear, nearest, slerp, custom)

---

## Core Concepts

### NativeContourSet — Input Geometry

Input to triangulation is provided as a `NativeContourSet`, which describes one or more closed polygon contours:

```csharp
public readonly struct NativeContourSet
{
    public readonly NativeArray<float3>.ReadOnly Positions;         // All unique point positions
    public readonly NativeArray<int>.ReadOnly ContourOffsets;       // Prefix-sum offsets into ContourPointIndices
    public readonly NativeArray<int>.ReadOnly ContourPointIndices;  // Index into Positions for each contour vertex
}
```

**Contour Offsets Format**: `ContourOffsets` has length `ContourCount + 1`. Contour `i` spans indices `ContourOffsets[i]` to `ContourOffsets[i+1]` in `ContourPointIndices`. The first offset must be 0, the last must equal `ContourPointIndices.Length`.

**Example**: A square with a triangular hole:
```
Positions:        [p0, p1, p2, p3, p4, p5, p6]  (7 unique points)
ContourOffsets:   [0, 4, 7]                       (2 contours)
ContourPointIndices: [0, 1, 2, 3, 4, 5, 6]       (outer quad + inner triangle)
```

**Validation**: `NativeContourSet.Validate()` checks:
- All arrays are created
- Offsets are monotonically non-decreasing
- First offset is 0, last equals indices length
- Each contour has at least 3 vertices
- All point indices are within `Positions` bounds

### NativeDetail — Output Geometry

The triangulation output is written into a `NativeDetail`, which is SangriaMesh's general-purpose geometry container supporting:
- **Points** with per-point attributes (Position is always present)
- **Vertices** (corners of primitives, referencing points)
- **Primitives** (triangles)

Points, vertices, and primitives correspond to Houdini's Point/Vertex/Primitive domains.

### TriangulationOptions — Configuration

```csharp
public struct TriangulationOptions
{
    public TriangulationWindingRule WindingRule;              // EvenOdd, NonZero, Positive, Negative, AbsGeqTwo
    public TriangulationContourOrientation ContourOrientation; // Original, Clockwise, CounterClockwise
    public float3 Normal;                                    // Projection normal (zero = auto-compute)
    public bool RemoveEmptyPolygons;                         // Filter zero-area faces from output
}
```

**Winding Rules** (identical to GLU/libtess2/LibTessDotNet):

| Rule | Description | Typical Use |
|------|-------------|-------------|
| `EvenOdd` | Region is inside if the winding number is odd | Simple polygons, CSG XOR |
| `NonZero` | Region is inside if the winding number is non-zero | Union of overlapping contours |
| `Positive` | Region is inside if the winding number is > 0 | CCW contours = fill, CW = hole |
| `Negative` | Region is inside if the winding number is < 0 | CW contours = fill |
| `AbsGeqTwo` | Region is inside if |winding number| >= 2 | Intersection of overlapping contours |

---

## API Reference

### Triangulation (Static Class)

#### TriangulateContours — Without Provenance

```csharp
public static CoreResult TriangulateContours(
    in NativeContourSet contours,
    ref NativeDetail output,
    in TriangulationOptions options = default);
```

Triangulates contours into `output`. No provenance data is generated. The internal provenance buffer is not allocated, so there is zero overhead compared to a non-provenance path.

**Preconditions**:
- `contours.Validate()` must return `CoreResult.Success`
- `output` must be empty (PointCount == VertexCount == PrimitiveCount == 0)

**Returns**: `CoreResult.Success` on success, `CoreResult.InvalidOperation` on precondition failure.

#### TriangulateContours — With Provenance

```csharp
public static CoreResult TriangulateContours(
    in NativeContourSet contours,
    ref NativeDetail output,
    out ProvenanceMap provenance,
    in TriangulationOptions options = default);
```

Same as above, but also produces a `ProvenanceMap` mapping each output point to its source input points with interpolation weights.

**Caller responsibility**: dispose `provenance` when done.

### ProvenanceRecord (Struct)

Fixed-size record describing the origin of a single output vertex.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct ProvenanceRecord
{
    public int Src0, Src1, Src2, Src3;    // Source point indices (into NativeContourSet.ContourPointIndices)
    public float W0, W1, W2, W3;          // Interpolation weights (normalized, sum = 1.0)
    public byte Count;                    // Number of active sources (1..4)
    public ProvenanceKind Kind;           // How this vertex was created
}
```

**Invariants** (always guaranteed):
1. `1 <= Count <= 4`
2. `W0 + W1 + ... + W(Count-1) = 1.0` (within floating-point tolerance)
3. All active `SrcN` are valid indices into the input positions array
4. Sources are sorted by `SrcN` ascending (deterministic ordering)
5. All active weights are positive

**Factory Methods**:

| Method | Description |
|--------|-------------|
| `Identity(int sourcePointId)` | Creates a record for an original input vertex (Count=1, W0=1.0) |
| `Intersection(src0, w0, src1, w1, src2, w2, src3, w3)` | Creates a record for an edge-edge intersection |
| `Combine(in a, in b, kind)` | Merges two records (up to 8 sources → coalesce → truncate to 4 → normalize) |
| `CombineWeighted(in a, wa, in b, wb, in c, wc, in d, wd, kind)` | Weighted flatten of 4 records (up to 16 sources → coalesce → truncate to 4 → normalize) |

### ProvenanceKind (Enum)

```csharp
public enum ProvenanceKind : byte
{
    Identity = 0,      // Original input point — no interpolation
    Intersection = 1,  // Created by edge-edge intersection (GetIntersectData)
    Merge = 2,         // Created by merging two coincident vertices (SpliceMergeVertices)
    EdgeSplit = 3,     // Created by splitting an edge at an existing vertex location
    Degenerate = 4     // Created in degenerate topology handling (ConnectLeftDegenerate)
}
```

### ProvenanceMap (Struct)

```csharp
public struct ProvenanceMap : IDisposable
{
    public NativeArray<ProvenanceRecord> Records;  // Indexed by output point index
    public int SourcePointCount;                   // Number of input source points
    public int OutputPointCount;                   // Number of output points
}
```

`Records[i]` corresponds to output point `i` in the `NativeDetail`.

### InterpolationMode (Enum)

```csharp
public enum InterpolationMode : byte
{
    Linear = 0,   // Weighted sum of source values (default)
    Nearest = 1,  // Copy from dominant source (highest weight)
    None = 2      // Skip — fill with zero / default
}
```

### InterpolationPolicy (Struct)

Controls which interpolation mode to use for each attribute.

```csharp
public struct InterpolationPolicy : IDisposable
{
    public static InterpolationPolicy Default { get; }  // All attributes = Linear
    
    public InterpolationPolicy(int capacity, Allocator allocator,
        InterpolationMode defaultMode = InterpolationMode.Linear);
    
    public void SetMode(int attributeId, InterpolationMode mode);
    public InterpolationMode GetMode(int attributeId);
}
```

### AttributeTransferOp (Static Class)

#### TransferPointAttributes — Type-Erased Bulk Transfer

```csharp
public static CoreResult TransferPointAttributes(
    ref NativeDetail source,
    ref NativeDetail destination,
    in ProvenanceMap provenance,
    in InterpolationPolicy policy = default);
```

Iterates all point attributes in `source` (except Position), creates matching attributes in `destination` if they don't exist, and blends values using provenance weights and the specified interpolation policy.

**How it works**:
1. For each attribute column in `source.PointAttributes`:
   - Skip `AttributeID.Position` (already set by triangulation)
   - Look up `InterpolationMode` from `policy`
   - If mode is `None`, skip
   - Create matching column in `destination` if needed
   - For each output point, read `ProvenanceRecord` and blend source values

**Blend logic per output point**:
- `Count == 0` → zero-fill (sentinel/missing provenance)
- `Count == 1` or `mode == Nearest` → direct copy from `Src0`
- `Count > 1` and `mode == Linear` → weighted sum: `dst = Σ(Wi * src[SrcI])`
- Non-float stride (stride % 4 != 0) → automatic fallback to Nearest

#### TransferAttribute\<T\> — Typed Single-Attribute Transfer

```csharp
public static CoreResult TransferAttribute<T>(
    NativeAttributeAccessor<T> sourceAccessor,
    NativeAttributeAccessor<T> destinationAccessor,
    in ProvenanceMap provenance,
    InterpolationMode mode = InterpolationMode.Linear) where T : unmanaged;
```

Transfers a single typed attribute using explicit accessors. Useful when you want fine-grained control or when working with a specific attribute.

---

## Provenance System

### What is Provenance?

Provenance (from the French *provenir*, "to come from") tracks the **origin** of each output vertex. For every point in the triangulation output, the provenance system records:
- **Which input points** it was derived from (1 to 4 source indices)
- **What weight** each source contributes (normalized, sum = 1.0)
- **How** it was created (identity, intersection, merge, edge split, or degenerate)

This is the same concept as:
- Houdini's **attribute interpolation provenance** in SOP operators
- Blender's **custom data interpolation** in BMesh operations
- Unreal's **PCG attribute transfer** metadata
- OpenSubdiv's **stencil tables** for limit surface evaluation

### Provenance Write Points (14 Total)

The sweep-line algorithm creates and modifies vertices at 14 distinct code locations. Each is instrumented with provenance tracking:

| Location | Kind | What Happens |
|----------|------|--------------|
| `AddContour` | Identity | Each input vertex enters the mesh with `{Src=pointIndex, W=1.0}` |
| `GetIntersectData` | Intersection | Two edges cross — new vertex is weighted blend of 4 endpoints. Uses `CombineWeighted` to correctly flatten composite provenance from cascading intersections |
| `SpliceMergeVertices` | Merge | Two coincident vertices are merged — provenance is combined. Fast path for identical Identity records |
| `CheckForRightSplice` ×2 | EdgeSplit | Edge is split at an existing vertex — provenance copied from nearest endpoint |
| `CheckForRightSplice` ×1 | Merge | Via `SpliceMergeVertices` |
| `CheckForLeftSplice` ×2 | EdgeSplit | Edge is split — provenance copied from destination endpoint |
| `CheckForIntersect` ×4 | EdgeSplit | Edge is split at event vertex position — provenance copied from event vertex |
| `CheckForIntersect` ×1 | Intersection | Double split + intersection calculation via `GetIntersectData` |
| `ConnectLeftDegenerate` ×1 | Degenerate | Degenerate topology handling — provenance copied from event vertex |

### Cascading Intersections

When multiple edges intersect in close proximity, a vertex created by one intersection can participate as a source in a subsequent intersection. This is handled by `CombineWeighted`:

1. Each of the 4 source vertices in `GetIntersectData` may itself have composite provenance (up to 4 sources each)
2. `CombineWeighted` **flattens** all provenance: it scales each source record by its intersection weight, collecting up to 16 source-weight pairs
3. **Coalesces** duplicate source IDs (summing their weights)
4. **Sorts** by weight descending, **truncates** to top 4 sources
5. **Sorts** by source ID ascending for deterministic ordering
6. **Normalizes** weights to sum to 1.0

This ensures that even deeply cascading intersections produce valid, normalized provenance. The truncation to 4 sources introduces a bounded approximation error that is typically negligible for practical attribute interpolation.

### Conditional Provenance Tracking

When the caller uses the overload **without** `out ProvenanceMap`, the internal `trackProvenance` flag is set to `false`. In this mode:
- The `vertexProvenance` buffer in `TessMesh` is **not allocated**
- All 14 provenance write points are **skipped** (guarded by `if (mesh.trackProvenance)`)
- Zero memory and compute overhead for the non-provenance path

---

## Attribute Transfer System

### Design Philosophy

The attribute transfer system follows the **attribute-agnostic** principle from professional DCC tools:

1. The **core** (triangulation) knows nothing about attribute semantics
2. **Provenance** is the bridge — pure data mapping from output to input
3. **Transfer** is a separate operation that reads provenance and interpolates attributes
4. **Policy** is user-configurable per-attribute

This is analogous to:
- **Houdini**: `attribtransfer` SOP / `attribinterpolate` VEX function
- **Blender**: `CustomData_bmesh_interp` in mesh operators
- **Unreal**: PCG `AttributeTransfer` node

### Type-Erased Blending

The blend engine operates on raw byte buffers with stride-based access, without knowing the concrete type `T`:

```
For stride = 12 (float3):
  dst[0..11] = w0 * src[Src0*12 .. Src0*12+11]
             + w1 * src[Src1*12 .. Src1*12+11]
             + w2 * src[Src2*12 .. Src2*12+11]
             + w3 * src[Src3*12 .. Src3*12+11]
```

This works correctly for any float-based type: `float`, `float2`, `float3`, `float4`, `Color`, `quaternion` (when using linear blend — for true spherical interpolation of quaternions, use Nearest or a future Slerp mode).

**Fallback**: If stride is not a multiple of 4 (non-float type), the system automatically falls back to Nearest mode (memcpy from dominant source).

### Supported Attribute Types for Linear Blend

Any `unmanaged` type whose memory layout consists entirely of `float` values:
- `float` (stride 4)
- `float2` (stride 8)
- `float3` (stride 12)
- `float4` (stride 16)
- `Color` (stride 16, if stored as 4 floats)
- `quaternion` (stride 16 — linear blend, not slerp)
- Custom structs containing only floats

---

## Usage Guide

### Basic Triangulation (No Provenance)

```csharp
// Prepare input
var positions = new NativeArray<float3>(4, Allocator.Temp);
positions[0] = new float3(0, 0, 0);
positions[1] = new float3(1, 0, 0);
positions[2] = new float3(1, 1, 0);
positions[3] = new float3(0, 1, 0);

var offsets = new NativeArray<int>(new[] { 0, 4 }, Allocator.Temp);
var indices = new NativeArray<int>(new[] { 0, 1, 2, 3 }, Allocator.Temp);

var contours = new NativeContourSet(positions, offsets, indices);
var output = new NativeDetail(16, Allocator.TempJob);

// Triangulate
CoreResult result = Triangulation.TriangulateContours(in contours, ref output);
Assert.AreEqual(CoreResult.Success, result);

// Read output
output.TryGetPointAccessor<float3>(AttributeID.Position, out var outPositions);
for (int i = 0; i < output.PointCount; i++)
    Debug.Log($"Point {i}: {outPositions[i]}");

// Cleanup
output.Dispose();
positions.Dispose();
offsets.Dispose();
indices.Dispose();
```

### Triangulation with Provenance

```csharp
CoreResult result = Triangulation.TriangulateContours(
    in contours, ref output, out ProvenanceMap provenance);

// Inspect provenance
for (int i = 0; i < provenance.OutputPointCount; i++)
{
    var record = provenance.Records[i];
    Debug.Log($"Point {i}: kind={record.Kind}, sources={record.Count}");
    if (record.Kind == ProvenanceKind.Identity)
        Debug.Log($"  Identity from input point {record.Src0}");
    else
        Debug.Log($"  Blended from {record.Count} sources");
}

provenance.Dispose();
```

### Full Attribute Transfer Workflow

```csharp
// 1. Prepare input with custom attributes
var source = new NativeDetail(positions.Length, Allocator.TempJob);
// ... populate source with positions and attributes ...
// Example: register a UV attribute
source.TryGetPointAccessor<float2>(myUvAttributeId, out var srcUVs);
// ... fill srcUVs ...

// 2. Triangulate with provenance
var output = new NativeDetail(64, Allocator.TempJob);
Triangulation.TriangulateContours(in contours, ref output, out ProvenanceMap provenance);

// 3. Configure interpolation policy
var policy = new InterpolationPolicy(4, Allocator.Temp);
policy.SetMode(myUvAttributeId, InterpolationMode.Linear);       // UV: blend
policy.SetMode(myMaterialIdAttribute, InterpolationMode.Nearest); // Material ID: copy from dominant
policy.SetMode(myDebugAttribute, InterpolationMode.None);         // Debug: skip

// 4. Transfer attributes
AttributeTransferOp.TransferPointAttributes(ref source, ref output, in provenance, in policy);

// 5. Cleanup
policy.Dispose();
provenance.Dispose();
output.Dispose();
source.Dispose();
```

### Polygon with Hole

```csharp
// Outer square + inner square hole
var positions = new NativeArray<float3>(8, Allocator.Temp);
positions[0] = new float3(0, 0, 0);  // outer
positions[1] = new float3(4, 0, 0);
positions[2] = new float3(4, 4, 0);
positions[3] = new float3(0, 4, 0);
positions[4] = new float3(1, 1, 0);  // hole (opposite winding)
positions[5] = new float3(1, 3, 0);
positions[6] = new float3(3, 3, 0);
positions[7] = new float3(3, 1, 0);

var offsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
var indices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);

var options = TriangulationOptions.Default;
options.WindingRule = TriangulationWindingRule.EvenOdd;  // EvenOdd treats second contour as hole
```

### Overlapping Contours (Union)

```csharp
var options = TriangulationOptions.Default;
options.WindingRule = TriangulationWindingRule.NonZero;
// NonZero winding = union of all contours (ignores overlap)
```

---

## Comparison with Industry Solutions

### Houdini (SideFX)

| Aspect | Houdini | SangriaMesh |
|--------|---------|-------------|
| Attribute model | Fully generic, any type on any domain (point/vertex/prim/detail) | Point-domain attributes via NativeDetail + AttributeStore |
| Provenance | Implicit in SOP processing, exposed via `attribinterpolate` VEX | Explicit `ProvenanceMap` with `ProvenanceRecord` per output point |
| Interpolation modes | Linear, nearest, slerp, custom VEX | Linear, nearest, none (custom via `TransferAttribute<T>`) |
| Core separation | SOP creates geo, attributes flow automatically | Triangulation creates topology + provenance, transfer is explicit |
| Winding rules | GLU-compatible | GLU-compatible (same 5 rules) |
| Performance model | Multi-threaded, compiled VEX | Burst-compatible, currently single-threaded transfer |
| Domain transfer | Point→vertex, vertex→point, prim→point, etc. | Point domain only (vertex/prim planned) |

**Key difference**: Houdini's attribute transfer is automatic (SOPs handle it internally). SangriaMesh makes it explicit, giving the caller full control over which attributes to transfer and how. This is more similar to Houdini's `attribtransfer` SOP used as a manual step.

### Blender (Geometry Nodes / BMesh)

| Aspect | Blender | SangriaMesh |
|--------|---------|-------------|
| Attribute model | Custom data layers on BMesh domains | AttributeStore columns on NativeDetail |
| Mesh representation | BMesh half-edge | LibTess-derived half-edge (NativeTessMesh) |
| Interpolation | `CustomData_bmesh_interp` with per-layer callbacks | Stride-based type-erased blend |
| Geometry Nodes | Transfer Attribute node with domain/mode selection | `AttributeTransferOp` with `InterpolationPolicy` |

### OpenSubdiv (Pixar)

| Aspect | OpenSubdiv | SangriaMesh |
|--------|-----------|-------------|
| Stencil model | Stencil tables: per-output-vertex list of (source, weight) pairs | `ProvenanceRecord`: fixed 4-slot (source, weight) |
| Max sources per vertex | Unlimited (variable-length stencils) | 4 (truncated from up to 16 after cascading intersections) |
| Use case | Subdivision surface evaluation | Constrained polygon tessellation |

### libtess2 / LibTessDotNet

| Aspect | LibTessDotNet | SangriaMesh |
|--------|--------------|-------------|
| Memory model | Managed C# classes, GC allocations | `UnsafeList`, `NativeArray`, zero managed alloc |
| Vertex data | `CombineCallback` with user `object` data | `ProvenanceRecord` with fixed-size source slots |
| Attribute handling | User implements `CombineCallback` to blend custom data | Decoupled: provenance + separate transfer op |
| Cascading intersections | User's `CombineCallback` receives full vertex data objects | `CombineWeighted` flattens composite provenance recursively |

---

## Performance Characteristics

### Memory

| Component | Per-Vertex Cost | Condition |
|-----------|----------------|-----------|
| Mesh vertex (SweepVertex) | ~44 bytes | Always |
| ProvenanceRecord (sweep buffer) | 36 bytes | Only when `trackProvenance=true` |
| ProvenanceMap (output) | 36 bytes | Only when provenance overload is used |
| Attribute transfer | 0 extra per vertex | Operates on existing buffers |

For a mesh with 10,000 output vertices:
- Without provenance: ~440 KB internal workspace
- With provenance: ~440 KB + 360 KB provenance = ~800 KB

### Compute

- **Sweep-line**: O(n log n) where n = input vertices + intersection events
- **Provenance write**: O(1) per vertex creation event (amortized)
- **CombineWeighted**: O(k²) where k ≤ 16 (up to 16 source pairs, coalesce + sort)
- **Attribute transfer**: O(outputPoints × attributeCount × stride/4)
- **No managed allocations** on the hot path

### Without Provenance

When using the non-provenance overload, there is **zero overhead**:
- No `vertexProvenance` buffer allocated
- All 14 provenance write points skipped via `if (mesh.trackProvenance)` guard
- Identical codegen to a non-instrumented sweep

---

## Design Decisions and Rationale

### Why Fixed 4-Slot ProvenanceRecord?

**Decision**: Each ProvenanceRecord stores at most 4 (sourceId, weight) pairs.

**Rationale**:
- `GetIntersectData` computes weights for exactly 4 vertices (orgUp, dstUp, orgLo, dstLo)
- Fixed size enables `StructLayout.Sequential` for Burst compatibility
- No heap allocation, no variable-length lists
- 36 bytes per record is cache-line friendly
- Truncation to 4 after cascading intersections introduces bounded error that is acceptable for practical attribute interpolation (UV, color, etc.)

**Trade-off**: In rare cases of deeply cascading intersections, sources beyond the top 4 by weight are discarded and weights are renormalized. This mirrors OpenSubdiv's approach of stencil compression for performance.

### Why Separate Transfer from Core?

**Decision**: The triangulation core never touches user attributes.

**Rationale**:
- **Single Responsibility**: the sweep algorithm is complex enough without attribute management
- **Zero-cost abstraction**: callers who don't need attribute transfer pay nothing
- **Composability**: different transfer policies can be applied to the same provenance
- **Type safety**: the core doesn't need to know about `float3` vs `Color` vs custom types
- **Industry standard**: this is how Houdini, Blender, and OpenSubdiv work

### Why Sort Sources by ID (Not by Weight)?

**Decision**: Active sources in ProvenanceRecord are sorted by sourceId ascending.

**Rationale**:
- **Determinism**: identical inputs always produce identical ProvenanceRecords regardless of sweep order
- **Testability**: exact expected values can be pre-calculated in unit tests
- **Comparison**: two ProvenanceRecords can be compared field-by-field

Weight ordering is used only temporarily during truncation (keep top-4 by weight), then sources are re-sorted by ID.

### Why trackProvenance Guard Instead of Always Writing?

**Decision**: All provenance writes are guarded by `if (mesh.trackProvenance)`.

**Rationale**:
- The non-provenance path is the legacy/default behavior — it should have zero overhead
- Writing 36 bytes per vertex × N vertices is measurable for large meshes
- The guard is a single branch per write site, highly predictable (always true or always false for the entire sweep)

---

## Limitations and Known Trade-offs

### 4-Source Truncation

When cascading intersections produce more than 4 unique source contributions, the least significant sources (by weight) are discarded. The remaining weights are renormalized to sum to 1.0.

**Impact**: Reconstructed attribute values may differ slightly from the "true" value for deeply intersected regions. For typical use cases (UV coordinates, vertex colors), the error is imperceptible.

**Quantification**: For two overlapping squares (8 input points), maximum reconstruction error for Position is < 0.05 units. For 3 overlapping squares (12 input points), some vertices may have reconstruction error up to ~0.35 units for Position. For attributes with less variation across vertices, the error is proportionally smaller.

### Point Domain Only

Attribute transfer currently operates only on the Point domain. Vertex-domain (per-corner) and Primitive-domain attributes are not yet supported. See Improvement Proposals for the roadmap.

### Single-Threaded Transfer

`AttributeTransferOp.BlendColumn` uses a sequential loop. For very large meshes with many attributes, this could benefit from `IJobParallelFor` parallelization. The current implementation is correct and suitable for typical mesh sizes encountered in 2D UI/vector graphics workflows.

### No Slerp Mode

Spherical linear interpolation (slerp) for quaternions and normals is not yet implemented. Use `Nearest` mode for these attributes, or implement custom blending via `TransferAttribute<T>`.

---

## Troubleshooting

### Common Issues

**Q: Provenance records have Count=0 for some output points**
A: This can happen for sentinel vertices that leaked into output. Check that your input contours are valid and don't produce degenerate topology.

**Q: Attribute transfer produces NaN values**
A: Check that source attribute values are finite. If provenance weights are correct (sum=1.0) but source values contain NaN, the blend result will be NaN. Also verify that source and destination NativeDetail instances are not the same object.

**Q: Weight sum is not exactly 1.0**
A: Floating-point precision allows for deviation up to ±1e-4. This is normal. The normalization step divides by the exact sum, but IEEE 754 arithmetic introduces rounding.

**Q: Position reconstruction doesn't match for intersection points**
A: This is expected for vertices created by cascading intersections where >4 unique sources were truncated to 4. Identity points always reconstruct exactly. Use the `ProvenanceKind` field to distinguish.

**Q: `CoreResult.InvalidOperation` returned from TriangulateContours**
A: Common causes:
1. Output NativeDetail is not empty (must have 0 points, vertices, primitives)
2. Input contours fail validation (check `NativeContourSet.Validate()`)
3. Contour has fewer than 3 vertices

**Q: How do I know which output points are new vs original?**
A: Check `ProvenanceKind`:
- `Identity` = original input point (1:1 mapping)
- `Intersection` = created by edge crossing
- `Merge` = two coincident input vertices merged
- `EdgeSplit` = edge was split at a vertex position
- `Degenerate` = degenerate topology handling
