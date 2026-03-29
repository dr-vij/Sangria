# SangriaMesh — NativeDetail

## Overview

`NativeDetail` is the central editable geometry container in SangriaMesh. It stores points, vertices, and primitives with sparse topology (alive flags + free lists) and typed attribute storage.

**Source files**: `NativeDetail.cs`, `NativeDetail.Utility.cs`, `NativeDetail.Compile.cs`, `NativeDetail.Primitive.cs`, `NativeDetail.PointVertex.cs`, `NativeDetail.AttributesResources.cs`, `NativeDetail.BurstJobs.cs`

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
int pointIndex = detail.AddPoint(new float3(1, 0, 0));  // Allocate point with position
int pointIndex2 = detail.AddPoint(pos, out ElementHandle handle);  // Also returns a handle
```

Points are allocated from a free list or by extending capacity. The returned index is stable until the point is removed.

### Point Position Access

```csharp
float3 pos = detail.GetPointPosition(pointIndex);
bool ok = detail.SetPointPosition(pointIndex, new float3(2, 0, 0));
```

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

### Modifying Primitives

```csharp
// Add a vertex to an existing primitive
bool added = detail.AddVertexToPrimitive(primitiveIndex, vertexIndex);

// Remove a vertex from a primitive by offset
bool removed = detail.RemoveVertexFromPrimitive(primitiveIndex, vertexOffset);

// Query vertex count for a primitive
int count = detail.GetPrimitiveVertexCount(primitiveIndex);

// Compute AABB for a primitive
bool ok = detail.GetPrimitiveBounds(primitiveIndex, out float3 bMin, out float3 bMax);
```

### Removing Elements

```csharp
// Remove a point (marks as dead)
bool removed = detail.RemovePoint(pointIndex);

// Remove a point with policy
bool removed = detail.RemovePoint(pointIndex, PointDeletePolicy.KeepReferencingVertices);

// Check before removing
bool canRemove = detail.CanRemovePoint(pointIndex, PointDeletePolicy.KeepReferencingVertices);
bool canRemove = detail.CanRemoveVertex(vertexIndex, VertexDeletePolicy.RemoveFromIncidentPrimitives);

// Remove a vertex with policy
bool removed = detail.RemoveVertex(vertexIndex, VertexDeletePolicy.RemoveFromIncidentPrimitives);

// Remove a primitive
bool removed = detail.RemovePrimitive(primitiveIndex);
```

#### Point Delete Policies

| Policy | Behavior |
|--------|----------|
| `KeepReferencingVertices` | Only marks the point as dead; vertices still reference the slot. |
| `RemoveReferencingVertices` | Also removes all vertices that reference this point. |

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
NativeSlice<int> primVerts = detail.GetPrimitiveVertices(primitiveIndex);
int vertCount = detail.GetPrimitiveVertexCount(primitiveIndex);

// Enumerate all alive elements
var alivePoints = new NativeList<int>(Allocator.Temp);
detail.GetAllValidPoints(alivePoints);

var aliveVertices = new NativeList<int>(Allocator.Temp);
detail.GetAllValidVertices(aliveVertices);

var alivePrimitives = new NativeList<int>(Allocator.Temp);
detail.GetAllValidPrimitives(alivePrimitives);
```

### Handle-Based Access

```csharp
// Get handles for elements
ElementHandle pointHandle = detail.GetPointHandle(pointIndex);
ElementHandle vertexHandle = detail.GetVertexHandle(vertexIndex);
ElementHandle primHandle = detail.GetPrimitiveHandle(primitiveIndex);

// Validate handles (guards against use-after-delete)
bool valid = detail.IsPointHandleValid(pointHandle);
bool valid = detail.IsVertexHandleValid(vertexHandle);
bool valid = detail.IsPrimitiveHandleValid(primHandle);
```

Handles store both an index and a generation counter, providing safe use-after-delete detection.

### Garbage Collection

Primitive storage may accumulate garbage after vertex removals from primitives. Use `CollectGarbage` to compact:

```csharp
// Compact primitive storage when garbage exceeds ratio
bool compacted = detail.CollectGarbage(minGarbageRatio: 0.25f);

// Query garbage state
int garbageLen = detail.PrimitiveGarbageLength;
bool hasGarbage = detail.PrimitiveHasGarbage;
int dataLen = detail.PrimitiveDataLength;
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

uint topoVersion = detail.TopologyVersion;      // Incremented on topology changes
uint attrVersion = detail.AttributeVersion;     // Incremented on attribute changes
```

Used by generators after populating geometry to indicate that downstream consumers (compilation, rendering) should refresh. Version counters can be compared to detect whether recompilation is needed.

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
