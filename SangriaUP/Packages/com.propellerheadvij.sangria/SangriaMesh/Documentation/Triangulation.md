# SangriaMesh — Triangulation

## Overview

SangriaMesh provides two triangulation systems:

1. **Contour Tessellation** — a sweep-line engine (LibTess port) that converts 2D contour inputs (including holes, overlapping contours, self-intersections) into indexed triangle meshes with provenance tracking.
2. **NativeDetailTriangulator** — converts N-gon `NativeDetail` geometry into triangle-only detail, with full attribute transfer across all domains.

**Source files**: `Triangulation.cs`, `NativeTess.cs`, `NativeTessSweep.cs`, `NativeTessMesh.cs`, `NativeTessGeom.cs`, `NativeTessDict.cs`, `NativeTessPQ.cs`, `NativeTessTypes.cs`, `NativeTessState.cs`, `ProvenanceTypes.cs`, `AttributeTransfer.cs`, `NativeDetailTriangulator.cs`

> **Detailed documentation**: See the comprehensive [Triangulation DOCUMENTATION.md](../Scripts/Triangulation/DOCUMENTATION.md) in the Triangulation source directory for full architecture details, provenance system internals, and comparison with industry solutions.

## Key Concepts

### Input: NativeContourSet

```csharp
public readonly struct NativeContourSet
{
    public readonly NativeArray<float3>.ReadOnly Positions;         // All unique point positions
    public readonly NativeArray<int>.ReadOnly ContourOffsets;       // Prefix-sum offsets
    public readonly NativeArray<int>.ReadOnly ContourPointIndices;  // Indices into Positions
}
```

Contours are defined by an array of 3D positions and prefix-sum offset arrays describing which positions belong to each closed contour.

### Output: NativeDetail + ProvenanceMap

The tessellator writes triangle primitives into a `NativeDetail`. Optionally, a `ProvenanceMap` is produced that tracks which input points contributed to each output point and with what weights.

### Configuration: TriangulationOptions

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `WindingRule` | `TriangulationWindingRule` | `EvenOdd` | Determines which regions are "inside" |
| `ContourOrientation` | `TriangulationContourOrientation` | `Original` | Forces contour winding direction |
| `Normal` | `float3` | `(0,0,0)` | Projection normal (zero = auto-compute) |
| `RemoveEmptyPolygons` | `bool` | `false` | Filters zero-area faces |

### Winding Rules

| Rule | Description |
|------|-------------|
| `EvenOdd` | Inside if winding number is odd (XOR behavior) |
| `NonZero` | Inside if winding number ≠ 0 (union of overlapping contours) |
| `Positive` | Inside if winding number > 0 |
| `Negative` | Inside if winding number < 0 |
| `AbsGeqTwo` | Inside if |winding number| ≥ 2 (intersection of overlapping contours) |

## Basic Usage

### Simple Triangulation

```csharp
// Prepare input: a square contour
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

// Use output...
output.Dispose();
positions.Dispose();
offsets.Dispose();
indices.Dispose();
```

### Polygon with Hole

```csharp
// Outer square + inner hole (opposite winding for EvenOdd rule)
var positions = new NativeArray<float3>(8, Allocator.Temp);
// positions[0..3] = outer square
// positions[4..7] = inner square (opposite winding)

var offsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
var indices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);

var options = TriangulationOptions.Default;
options.WindingRule = TriangulationWindingRule.EvenOdd;

Triangulation.TriangulateContours(in contours, ref output, in options);
```

### Lightweight Raw Triangulation

For temporary on-the-fly triangulation (e.g. ray-casting against a BVH tree) where no attributes or `NativeDetail` are needed, use `TriangulateRaw`. It writes positions and triangle indices directly into caller-owned `NativeList`s:

```csharp
var positions = new NativeArray<float3>(4, Allocator.Temp);
positions[0] = new float3(0, 0, 0);
positions[1] = new float3(4, 0, 0);
positions[2] = new float3(4, 4, 0);
positions[3] = new float3(0, 4, 0);

var offsets = new NativeArray<int>(new[] { 0, 4 }, Allocator.Temp);
var indices = new NativeArray<int>(new[] { 0, 1, 2, 3 }, Allocator.Temp);

var contours = new NativeContourSet(positions, offsets, indices);
var outPositions = new NativeList<float3>(Allocator.TempJob);
var outIndices = new NativeList<int>(Allocator.TempJob);

CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);

// outPositions contains triangle vertex positions
// outIndices contains triangle index triples (every 3 values = one triangle)
// Use for ray-casting, BVH queries, etc.

outPositions.Dispose();
outIndices.Dispose();
positions.Dispose();
offsets.Dispose();
indices.Dispose();
```

### With Provenance and Attribute Transfer

```csharp
// Triangulate with provenance tracking
CoreResult result = Triangulation.TriangulateContours(
    in contours, ref output, out ProvenanceMap provenance);

// Transfer attributes from source to output
var policy = new InterpolationPolicy(4, Allocator.Temp);
policy.SetMode(myUvAttribute, InterpolationMode.Linear);
policy.SetMode(myMaterialId, InterpolationMode.Nearest);

AttributeTransferOp.TransferPointAttributes(
    ref source, ref output, in provenance, in policy);

policy.Dispose();
provenance.Dispose();
```

## Provenance System

The provenance system tracks the origin of every output vertex:

### ProvenanceRecord

Each output point has a `ProvenanceRecord` with:
- Up to 4 source point indices (`Src0`–`Src3`)
- Corresponding weights (`W0`–`W3`, normalized to sum = 1.0)
- A `ProvenanceKind` indicating how the vertex was created

### ProvenanceKind

| Kind | Description |
|------|-------------|
| `Identity` | Original input point (1:1 mapping) |
| `Intersection` | Created by edge-edge crossing |
| `Merge` | Two coincident vertices merged |
| `EdgeSplit` | Edge split at existing vertex |
| `Degenerate` | Degenerate topology handling |

## Attribute Transfer

### InterpolationMode

| Mode | Behavior |
|------|----------|
| `Linear` | Weighted sum of source values (default for float-based types) |
| `Nearest` | Copy from dominant source (highest weight) |
| `None` | Skip attribute (fill with zero) |

### Supported Types for Linear Blend

Any `unmanaged` type with float-only memory layout: `float`, `float2`, `float3`, `float4`, `Color`, `quaternion`.

Non-float types automatically fall back to `Nearest` mode.

## Architecture

```
User Code
    │
    ▼
Triangulation.TriangulateContours()
    │
    ├── NativeTessSweep (sweep-line decomposition)
    ├── NativeTessMesh (half-edge workspace)
    ├── ProvenanceMap (output vertex origins)
    │
    ▼
AttributeTransferOp.TransferPointAttributes()
    │
    ├── InterpolationPolicy (per-attribute mode)
    └── Stride-based type-erased blending
```

The core tessellator never reads or writes user attributes — provenance is the bridge between topology operations and attribute transfer.

## NativeDetailTriangulator

**Class**: `NativeDetailTriangulator` (static)

Converts all N-gon primitives in a `NativeDetail` into triangles. Triangles (3-vertex primitives) pass through unchanged. All point, vertex, and primitive attributes are transferred.

### TriangulationMode

| Mode | Description |
|------|-------------|
| `Fan` | Simple fan from first vertex. Fast, correct only for convex polygons. |
| `EarClipping` | Ear-clipping algorithm. Handles concave simple polygons. Default. |
| `Tess` | Full sweep-line tessellation (LibTess). Handles complex/non-planar polygons. Accepts `TriangulationOptions`. |

### Triangulate

Triangulates all primitives from a source detail into a new output detail:

```csharp
var output = new NativeDetail(source.PointCount, source.VertexCount, source.PrimitiveCount, Allocator.TempJob);

CoreResult result = NativeDetailTriangulator.Triangulate(
    ref source,
    ref output,
    mode: TriangulationMode.EarClipping,
    policy: InterpolationPolicy.Default,
    tessOptions: default);

// output now contains only triangle primitives
output.Dispose();
```

- `outputDetail` must be a fresh, empty `NativeDetail`.
- Returns `CoreResult.InvalidOperation` if any primitive has fewer than 3 vertices.

### TriangulateInPlace

Convenience method that triangulates in-place by creating a temporary detail and swapping:

```csharp
CoreResult result = NativeDetailTriangulator.TriangulateInPlace(
    ref detail,
    mode: TriangulationMode.EarClipping,
    outputAllocator: Allocator.Persistent);
// detail is now replaced with triangulated version
```

### Attribute Transfer

All three modes perform full attribute transfer:
- **Point attributes**: Transferred via provenance (identity mapping for Fan/EarClipping, interpolated for Tess).
- **Vertex attributes**: Transferred per-corner. For Fan/EarClipping, vertex semantics are preserved 1:1. For Tess, vertex attributes are derived from the source vertices.
- **Primitive attributes**: Copied from the source primitive to each output triangle generated from it.

## Attribute Transfer API

**Class**: `AttributeTransferOp` (static)

Transfers attributes between details using a `ProvenanceMap` and `InterpolationPolicy`.

```csharp
// Transfer all point attributes
AttributeTransferOp.TransferPointAttributes(ref src, ref dst, in provenance, in policy);

// Transfer all vertex attributes
AttributeTransferOp.TransferVertexAttributes(ref src, ref dst, vertexMap, in policy);

// Transfer all primitive attributes
AttributeTransferOp.TransferPrimitiveAttributes(ref src, ref dst, primitiveMap, in policy);

// Transfer a single typed attribute
AttributeTransferOp.TransferAttribute<float3>(srcAccessor, dstAccessor, in provenance, InterpolationMode.Linear);
```

## Performance

- **Sweep-line**: O(n log n) complexity
- **Zero managed allocations** on the hot path
- **Conditional provenance**: When not requested, provenance tracking has zero overhead
- **Memory**: ~44 bytes per mesh vertex + 36 bytes per vertex for provenance (when enabled)
- **NativeDetailTriangulator**: Fan is O(n), EarClipping is O(n²) per polygon, Tess is O(n log n)
