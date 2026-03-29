# SangriaMesh — NativeDetail

## Overview

`NativeDetail` is the central editable geometry container in SangriaMesh. It stores points, vertices, and primitives with sparse topology (alive flags + free lists) and typed attribute storage.

**Source files**: `NativeDetail.cs`, `NativeDetail.Utility.cs`, `NativeDetail.Compile.cs`, `NativeDetail.Primitive.cs`, `NativeDetail.PointVertex.cs`, `NativeDetail.AttributesResources.cs`

## Construction

```csharp
// Create with initial capacities
var detail = new NativeDetail(
    pointCapacity: 100,
    vertexCapacity: 200,
    primitiveCapacity: 50,
    allocator: Allocator.Persistent);

// Simplified: single capacity for all domains
var detail = new NativeDetail(capacity: 100, allocator: Allocator.TempJob);
```

The `Position` attribute (`float3`) is automatically created on the Point domain during construction.

## Topology Operations

### Adding Points

```csharp
int pointIndex = detail.AddPoint();  // Returns index of newly allocated point
```

Points are allocated from a free list or by extending capacity. The returned index is stable until the point is removed.

### Adding Vertices

```csharp
int vertexIndex = detail.AddVertex(pointIndex);  // Creates vertex referencing a point
```

Each vertex must reference a valid, alive point.

### Adding Primitives

```csharp
// Add a triangle (3 vertex indices)
var vertices = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
int primitiveIndex = detail.AddPrimitive(vertices);
vertices.Dispose();

// Add a quad (4 vertex indices)
var quadVerts = new NativeArray<int>(new[] { v0, v1, v2, v3 }, Allocator.Temp);
int quadIndex = detail.AddPrimitive(quadVerts);
quadVerts.Dispose();
```

Primitives support N-gon topology (any number of vertices ≥ 3).

### Removing Elements

```csharp
// Remove a point (marks as dead)
bool removed = detail.RemovePoint(pointIndex);

// Remove a vertex with policy
bool removed = detail.RemoveVertex(vertexIndex, VertexDeletePolicy.RemoveFromIncidentPrimitives);

// Remove a primitive
bool removed = detail.RemovePrimitive(primitiveIndex);
```

#### Vertex Delete Policies

| Policy | Behavior |
|--------|----------|
| `RemoveFromIncidentPrimitives` | Removes the vertex from all primitives that reference it. Primitives with < 3 remaining vertices are also removed. |
| `RemoveVertexOnly` | Only marks the vertex as dead; does not modify primitives. |

### Querying Topology

```csharp
// Counts (alive elements only)
int points = detail.PointCount;
int vertices = detail.VertexCount;
int primitives = detail.PrimitiveCount;

// Capacities (including dead slots)
int pointCap = detail.PointCapacity;
int vertexCap = detail.VertexCapacity;
int primitiveCap = detail.PrimitiveCapacity;

// Alive checks
bool alive = detail.IsPointAlive(pointIndex);
bool alive = detail.IsVertexAlive(vertexIndex);
bool alive = detail.IsPrimitiveAlive(primitiveIndex);

// Vertex → Point mapping
int pointIndex = detail.GetVertexPoint(vertexIndex);

// Primitive vertices
NativeArray<int>.ReadOnly primVerts = detail.GetPrimitiveVertices(primitiveIndex);

// Enumerate all alive elements
var alivePoints = new NativeList<int>(Allocator.Temp);
detail.GetAllValidPoints(alivePoints);

var aliveVertices = new NativeList<int>(Allocator.Temp);
detail.GetAllValidVertices(aliveVertices);

var alivePrimitives = new NativeList<int>(Allocator.Temp);
detail.GetAllValidPrimitives(alivePrimitives);
```

## Dense Topology Allocation

For bulk construction (used by generators), `NativeDetail` supports a fast path that allocates contiguous blocks without per-element overhead:

```csharp
detail.AllocateDenseTopologyUnchecked(
    pointCount: 8,
    vertexCount: 24,
    primitiveCount: 0,           // primitives added separately
    prepareTriangleStorage: false,
    initializeVertexToPoint: false);
```

This pre-allocates contiguous ranges and marks all elements as alive in one operation.

## Clearing

```csharp
detail.Clear();  // Resets all counts to zero, marks all elements as dead
```

Capacity is preserved; no reallocation occurs.

## Change Tracking

```csharp
detail.MarkTopologyAndAttributeChanged();  // Signals that topology or attributes have changed
```

Used by generators after populating geometry to indicate that downstream consumers (compilation, rendering) should refresh.

## Compilation

```csharp
NativeCompiledDetail compiled = detail.Compile(Allocator.TempJob);
// ... use compiled ...
compiled.Dispose();
```

See [Compilation](Compilation.md) for details on the sparse-to-dense packing pipeline.

## Disposal

```csharp
detail.Dispose();  // Frees all native memory (topology, attributes, resources)
```

`NativeDetail` implements `IDisposable`. Always dispose when done. Double-dispose is safe (no-op).

## Unsafe Access

For performance-critical inner loops, `NativeDetail` provides unsafe pointer access:

```csharp
unsafe
{
    int* vertexToPointPtr = detail.GetVertexToPointPointerUnchecked();
    // Direct pointer to vertex-to-point mapping array
}
```

These methods bypass bounds checking and are intended for use within generators and Burst-compiled jobs.

## Implementation Notes

- `NativeDetail` is a `struct` implemented as partial classes across multiple files
- Topology uses **alive flags** (byte arrays) rather than indirection layers
- Points and vertices are indexed by dense slot indices; "alive" slots form a sparse subset
- Free list management enables O(1) allocation of recycled slots
- The `Position` attribute is always present on the Point domain (created automatically)
