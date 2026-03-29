# SangriaMesh — Compilation Pipeline

## Overview

The compilation pipeline transforms a sparse, editable `NativeDetail` into a dense, read-only `NativeCompiledDetail`. This process packs only alive elements into contiguous arrays, remaps all indices, and produces a snapshot optimized for read-only consumption (rendering, export, spatial queries).

**Source files**: `NativeDetail.Compile.cs`, `NativeCompiledDetail.cs`

## Why Compile?

| Editable (`NativeDetail`) | Compiled (`NativeCompiledDetail`) |
|---------------------------|-----------------------------------|
| Sparse storage with alive flags | Dense, contiguous arrays |
| O(1) add/remove operations | Read-only, no modifications |
| May contain dead slots (gaps) | No gaps, minimal memory |
| Index space includes dead elements | Index space is compact (0..N-1) |
| Suitable for editing | Suitable for rendering and export |

## Basic Usage

```csharp
// Compile editable detail to read-only snapshot
NativeCompiledDetail compiled = detail.Compile(Allocator.TempJob);

// Use compiled data
Debug.Log($"Points: {compiled.PointCount}");
Debug.Log($"Vertices: {compiled.VertexCount}");
Debug.Log($"Primitives: {compiled.PrimitiveCount}");

// Access compiled attributes
compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var positions);

// Access compiled resources
compiled.TryGetResource<float>(myResourceId, out float value);

// Dispose when done
compiled.Dispose();
```

## Compilation Process

The compilation performs these steps:

### 1. Build Remapping Tables

For each domain (Point, Vertex, Primitive), the compiler builds a sparse-to-dense index map:

```
Sparse indices:  [0:alive, 1:dead, 2:alive, 3:alive, 4:dead]
Dense indices:   [0 → 0, 2 → 1, 3 → 2]
Remap table:     [0, -1, 1, 2, -1]
```

### 2. Pack Topology

- **VertexToPointDense**: For each alive vertex, stores its point index remapped to dense space
- **PrimitiveOffsetsDense**: Prefix-sum offsets for primitive vertex lists (length = PrimitiveCount + 1)
- **PrimitiveVerticesDense**: Concatenated vertex indices for all primitives, remapped to dense space

### 3. Detect Triangle-Only Topology

If every alive primitive has exactly 3 vertices, `IsTriangleOnlyTopology` is set to `true`. This enables fast paths in Unity Mesh conversion that skip polygon triangulation.

### 4. Compile Attributes

Each `AttributeStore` (Point, Vertex, Primitive) is compiled into a `CompiledAttributeSet`:
- Only alive elements are packed
- Data is stored in a single contiguous `NativeArray<byte>` with per-attribute offsets
- Descriptors record attribute ID, type hash, stride, and byte offset

### 5. Compile Resources

The `ResourceRegistry` is compiled into a `CompiledResourceSet`:
- All resource blobs are packed into a single byte array
- Descriptors record resource ID, type hash, size, and byte offset

## Dense vs. Contiguous Fast Path

When all elements are alive and contiguous (no removals have occurred), the compiler uses a **fast path**:
- Skips remapping entirely
- Copies topology arrays directly
- Attributes are copied without per-element alive checks

This is common after generator use (Box, Sphere) where no editing has been performed.

## NativeCompiledDetail API

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `PointCount` | `int` | Number of compiled points |
| `VertexCount` | `int` | Number of compiled vertices |
| `PrimitiveCount` | `int` | Number of compiled primitives |
| `IsTriangleOnlyTopology` | `bool` | `true` if all primitives are triangles |
| `IsCreated` | `bool` | `true` if the compiled detail has valid data |
| `IsDisposed` | `bool` | `true` if already disposed |

### Topology Access

```csharp
// Dense vertex-to-point mapping
NativeArray<int>.ReadOnly vtpDense = compiled.VertexToPointDense;

// Dense primitive offsets (prefix-sum, length = PrimitiveCount + 1)
NativeArray<int>.ReadOnly offsets = compiled.PrimitiveOffsetsDense;

// Dense primitive vertex indices
NativeArray<int>.ReadOnly primVerts = compiled.PrimitiveVerticesDense;

// Iterate primitive vertices
for (int prim = 0; prim < compiled.PrimitiveCount; prim++)
{
    int start = offsets[prim];
    int end = offsets[prim + 1];
    for (int i = start; i < end; i++)
    {
        int vertexIndex = primVerts[i];
        int pointIndex = vtpDense[vertexIndex];
    }
}
```

### Attribute Access

```csharp
// Get attribute descriptors for a domain
var descriptors = compiled.GetAttributeDescriptors(MeshDomain.Point);

// Typed accessor
compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var accessor);
float3 pos = accessor[0];

// Raw (type-erased) accessor
compiled.TryGetRawAttributeAccessor(MeshDomain.Vertex, AttributeID.UV0, out var rawAccessor);
```

### Resource Access

```csharp
compiled.TryGetResource<float>(myResourceId, out float value);
compiled.TryGetResource<int>(anotherResourceId, out int intValue);
```

## Lifecycle

- `NativeCompiledDetail` owns all its native arrays
- It must be disposed via `Dispose()` when no longer needed
- Pass by `in` or `ref` to avoid copying the struct (which would alias ownership)
- Double-dispose is safe (no-op after first dispose)

```csharp
// CORRECT: pass by in/ref
void ProcessMesh(in NativeCompiledDetail compiled) { ... }

// INCORRECT: copying by value aliases ownership
NativeCompiledDetail copy = compiled;  // Don't do this!
```

## Performance Notes

- Compilation allocates new native arrays for all output data
- For real-time use, compile once per frame (or when geometry changes)
- The fast path (contiguous topology) avoids per-element remapping overhead
- Use `Allocator.TempJob` for frame-scoped compilations
- Use `Allocator.Persistent` for long-lived compiled snapshots
