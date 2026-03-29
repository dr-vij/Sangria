# SangriaMesh — Contour Triangulation

## Overview

SangriaMesh includes a full sweep-line contour tessellation engine — a native, Burst-compatible port of LibTessDotNet. It converts 2D contour inputs (including contours with holes, overlapping contours, self-intersections, and degenerate geometry) into indexed triangle meshes stored in `NativeDetail`.

The engine features a provenance tracking system that records the origin of every output vertex, enabling attribute transfer (UV, color, normals) as a separate, composable operation.

**Source files**: `Triangulation.cs`, `NativeTess.cs`, `NativeTessSweep.cs`, `NativeTessMesh.cs`, `NativeTessGeom.cs`, `NativeTessDict.cs`, `NativeTessPQ.cs`, `NativeTessTypes.cs`, `NativeTessState.cs`, `ProvenanceTypes.cs`, `AttributeTransfer.cs`

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

## Performance

- **Sweep-line**: O(n log n) complexity
- **Zero managed allocations** on the hot path
- **Conditional provenance**: When not requested, provenance tracking has zero overhead
- **Memory**: ~44 bytes per mesh vertex + 36 bytes per vertex for provenance (when enabled)
